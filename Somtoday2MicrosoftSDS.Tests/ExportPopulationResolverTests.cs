using Microsoft.Extensions.Logging;
using Somtoday2MicrosoftSDS.Helpers;
using Somtoday2MicrosoftSDS.Models;
using Xunit;

namespace Somtoday2MicrosoftSDS.Tests;

public class ExportPopulationResolverTests
{
    private static readonly DateOnly RunDate = new(2026, 8, 1);

    [Fact]
    public void ResolvesOnlyEligibleClassesAndTheirPeopleAndGuardians()
    {
        Guid teacherId = Guid.NewGuid();
        Guid excludedTeacherId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid excludedStudentId = Guid.NewGuid();
        Guid danglingTeacherId = Guid.NewGuid();
        Guid danglingStudentId = Guid.NewGuid();
        OuderVerzorger includedGuardian = CreateGuardian(studentId, excludedStudentId);
        OuderVerzorger orphanGuardian = CreateGuardian(excludedStudentId);
        OuderVerzorger guardianWithoutConsent = CreateGuardian(studentId);
        guardianWithoutConsent.WenstContactViaEMail = false;

        VestigingModel model = CreateModel(
            classes:
            [
                CreateClass("Eligible", [teacherId, danglingTeacherId], [studentId, danglingStudentId]),
                CreateClass("Teacher only", [teacherId], [danglingStudentId]),
                CreateClass("Student only", [danglingTeacherId], [studentId]),
                CreateClass("   ", [excludedTeacherId], [excludedStudentId]),
                CreateClass(string.Empty, [excludedTeacherId], [excludedStudentId]),
                CreateClass(null, [excludedTeacherId], [excludedStudentId]),
                new Lesgroep { Uuid = Guid.NewGuid(), Naam = "No references", Docenten = null, Leerlingen = null }
            ],
            teachers:
            [
                new Medewerker { Uuid = teacherId, Emailadres = string.Empty },
                new Medewerker { Uuid = excludedTeacherId, Emailadres = "excluded-teacher@example.test" }
            ],
            students:
            [
                new Leerling { Uuid = studentId, Emailadres = string.Empty },
                new Leerling { Uuid = excludedStudentId, Emailadres = "excluded-student@example.test" }
            ],
            guardians: [includedGuardian, orphanGuardian, guardianWithoutConsent]);

        ResolvedExportPopulation population = ExportPopulationResolver.Resolve(model);

        ResolvedClass resolvedClass = Assert.Single(population.Classes);
        Assert.Equal("Eligible", resolvedClass.Source.Naam);
        Assert.Equal(teacherId, Assert.Single(resolvedClass.Teachers).Uuid);
        Assert.Equal(studentId, Assert.Single(resolvedClass.Students).Uuid);
        Assert.Equal(teacherId, Assert.Single(population.Teachers).Uuid);
        Assert.Equal(studentId, Assert.Single(population.Students).Uuid);

        ResolvedGuardian resolvedGuardian = Assert.Single(population.Guardians);
        Assert.Equal(includedGuardian.Uuid, resolvedGuardian.Source.Uuid);
        Assert.Equal(studentId, Assert.Single(resolvedGuardian.StudentIds));

        SDScsvV1 v1 = new SDScsvHelperV1(population, RunDate).ConvertToSDSCSV();
        SDScsvV2 v2 = new SDScsvHelperV2(population, RunDate).ConvertToSDSCSV();
        Assert.Single(v1.Guardianrelationship, relationship => relationship.SISid == studentId.ToString());
        Assert.DoesNotContain(v1.User, guardian => guardian.SISid == orphanGuardian.Uuid.ToString());
        Assert.Single(v2.Relationships, relationship => relationship.userSourcedId == studentId.ToString());
        Assert.DoesNotContain(v2.Users, user => user.sourcedId == orphanGuardian.Uuid.ToString());
        Assert.DoesNotContain(v2.Roles, role => role.userSourcedId == orphanGuardian.Uuid.ToString());
    }

    [Fact]
    public void SharedPopulationKeepsV1AndV2PersonAndClassSetsAligned()
    {
        Guid teacherId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid unassignedTeacherId = Guid.NewGuid();
        Guid unassignedStudentId = Guid.NewGuid();
        VestigingModel model = CreateModel(
            classes:
            [
                CreateClass("Class A", [teacherId], [studentId]),
                CreateClass("Class B", [teacherId], [studentId])
            ],
            teachers:
            [
                new Medewerker { Uuid = teacherId, Emailadres = "teacher@example.test" },
                new Medewerker { Uuid = unassignedTeacherId, Emailadres = "unassigned-teacher@example.test" }
            ],
            students:
            [
                new Leerling { Uuid = studentId, Emailadres = "student@example.test" },
                new Leerling { Uuid = unassignedStudentId, Emailadres = "unassigned-student@example.test" }
            ]);

        ResolvedExportPopulation population = ExportPopulationResolver.Resolve(model);
        SDScsvV1 v1 = new SDScsvHelperV1(population, RunDate).ConvertToSDSCSV();
        SDScsvV2 v2 = new SDScsvHelperV2(population, RunDate).ConvertToSDSCSV();
        CapturingProgramLogger logger = new();

        Assert.Equal(2, population.Classes.Count);
        Assert.Single(population.Teachers);
        Assert.Single(population.Students);
        Assert.Single(v1.Teachers);
        Assert.Single(v1.Students);
        Assert.Equal(2, v2.Users.Count);
        Assert.Equal(
            v1.Sections.Select(section => section.SISid).OrderBy(id => id),
            v2.Classes.Select(sdsClass => sdsClass.sourcedId).OrderBy(id => id));
        Assert.Equal(
            v1.Teachers.Select(teacher => teacher.SISid),
            v2.Roles.Where(role => role.role == "staff").Select(role => role.userSourcedId));
        Assert.Equal(
            v1.Students.Select(student => student.SISid),
            v2.Roles.Where(role => role.role == "student").Select(role => role.userSourcedId));
        Assert.DoesNotContain(v2.Users, user => user.sourcedId == unassignedTeacherId.ToString());
        Assert.DoesNotContain(v2.Users, user => user.sourcedId == unassignedStudentId.ToString());
        Assert.DoesNotContain(v2.Enrollments, enrollment =>
            enrollment.userSourcedId == unassignedTeacherId.ToString()
            || enrollment.userSourcedId == unassignedStudentId.ToString());
        Assert.DoesNotContain(v1.TeacherRosters, enrollment => enrollment.SISTeacherid == unassignedTeacherId.ToString());
        Assert.DoesNotContain(v1.StudentEnrollments, enrollment => enrollment.SISStudentid == unassignedStudentId.ToString());
        Assert.True(Program.ShouldPublishLocation(population, "Test school", logger));
        Assert.Empty(logger.Messages);
    }

    [Fact]
    public void PopulationWithoutEligibleClassIsNotPublishable()
    {
        Guid teacherId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        VestigingModel model = CreateModel(
            classes: [CreateClass("Teacher only", [teacherId], [])],
            teachers: [new Medewerker { Uuid = teacherId }],
            students: [new Leerling { Uuid = studentId }]);

        ResolvedExportPopulation population = ExportPopulationResolver.Resolve(model);
        CapturingProgramLogger logger = new();

        Assert.Empty(population.Classes);
        Assert.Empty(population.Teachers);
        Assert.Empty(population.Students);
        Assert.Empty(population.Guardians);
        Assert.False(Program.ShouldPublishLocation(population, "Test school", logger));
        Assert.Contains(logger.Messages, message =>
            message.Contains("Skipping Test school/Test location", StringComparison.Ordinal)
            && message.Contains("existing Blob output is unchanged", StringComparison.Ordinal));
    }

    private static VestigingModel CreateModel(
        List<Lesgroep> classes,
        List<Medewerker> teachers,
        List<Leerling> students,
        List<OuderVerzorger> guardians = null)
    {
        return new VestigingModel
        {
            Vestiging = new Vestiging
            {
                Uuid = Guid.NewGuid(),
                Naam = "Test location",
                Afkorting = "LOC"
            },
            Lesgroepen = classes,
            Medewerkers = teachers,
            Leerlingen = students,
            OuderVerzorgers = guardians ?? []
        };
    }

    private static Lesgroep CreateClass(
        string name,
        IEnumerable<Guid> teacherIds,
        IEnumerable<Guid> studentIds)
    {
        return new Lesgroep
        {
            Uuid = Guid.NewGuid(),
            Naam = name,
            Docenten = teacherIds.ToList(),
            Leerlingen = studentIds.Select(id => new LeerlingVestiging { Uuid = id }).ToList(),
            Vaknaam = "Subject",
            Onderwijssoort = "Education"
        };
    }

    private static OuderVerzorger CreateGuardian(params Guid[] studentIds)
    {
        return new OuderVerzorger
        {
            Uuid = Guid.NewGuid(),
            Emailadres = $"{Guid.NewGuid():N}@example.test",
            WenstContactViaEMail = true,
            Voorletters = "A.",
            Achternaam = "Guardian",
            Leerlingen_van_vestiging = studentIds
        };
    }

    private sealed class CapturingProgramLogger : ILogger<Program>
    {
        internal List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class NullScope : IDisposable
    {
        internal static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

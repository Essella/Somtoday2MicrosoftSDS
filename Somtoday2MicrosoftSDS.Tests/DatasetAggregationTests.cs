using Somtoday2MicrosoftSDS.Helpers;
using Somtoday2MicrosoftSDS.Models;
using Xunit;

namespace Somtoday2MicrosoftSDS.Tests;

public sealed class DatasetAggregationTests
{
    private static readonly DateOnly RunDate = new(2026, 8, 1);

    [Fact]
    public void SharedPeopleArePerLocationInV1AndDeduplicatedWithMultipleRolesInV2()
    {
        Medewerker teacher = Teacher(Guid.NewGuid(), "teacher@example.test");
        Leerling student = Student(Guid.NewGuid(), "student@example.test");
        OuderVerzorger guardian = Guardian(Guid.NewGuid(), "guardian@example.test", student.Uuid);
        ResolvedExportPopulation first = Population("A", "A-Class", teacher, student, guardian);
        ResolvedExportPopulation second = Population("B", "B-Class", teacher, student, guardian);

        SDScsvV1 v1 = new SDScsvHelperV1([first, second], RunDate).ConvertToSDSCSV();
        SDScsvV2 v2 = new SDScsvHelperV2([first, second], RunDate).ConvertToSDSCSV();

        Assert.Equal(
            [first.Vestiging.Uuid.ToString(), second.Vestiging.Uuid.ToString()],
            v1.Schools.Select(school => school.SISid));
        Assert.Equal(
            [first.Vestiging.Uuid.ToString(), second.Vestiging.Uuid.ToString()],
            v2.Orgs.Select(organization => organization.sourcedId));
        Assert.Equal(2, v1.Teachers.Count(row => row.SISid == teacher.Uuid.ToString()));
        Assert.Equal(2, v1.Students.Count(row => row.SISid == student.Uuid.ToString()));
        Assert.Single(v1.User, row => row.SISid == guardian.Uuid.ToString());
        Assert.Single(v1.Guardianrelationship);

        Assert.Single(v2.Users, row => row.sourcedId == teacher.Uuid.ToString());
        Assert.Single(v2.Users, row => row.sourcedId == student.Uuid.ToString());
        Assert.Single(v2.Users, row => row.sourcedId == guardian.Uuid.ToString());
        Assert.Equal(2, v2.Roles.Count(row => row.userSourcedId == teacher.Uuid.ToString()));
        Assert.Equal(2, v2.Roles.Count(row => row.userSourcedId == student.Uuid.ToString()));
        Assert.Equal(2, v2.Roles.Count(row => row.userSourcedId == guardian.Uuid.ToString()));
        Assert.Single(v2.Relationships);
    }

    [Fact]
    public void DifferentPeopleWithSameUsernameArePassedThrough()
    {
        Medewerker firstTeacher = Teacher(Guid.NewGuid(), "shared@example.test");
        Medewerker secondTeacher = Teacher(Guid.NewGuid(), "shared@example.test");
        ResolvedExportPopulation first = Population(
            "A",
            "A-Class",
            firstTeacher,
            Student(Guid.NewGuid(), "first@example.test"));
        ResolvedExportPopulation second = Population(
            "B",
            "B-Class",
            secondTeacher,
            Student(Guid.NewGuid(), "second@example.test"));

        SDScsvV1 v1 = new SDScsvHelperV1([first, second], RunDate).ConvertToSDSCSV();
        SDScsvV2 v2 = new SDScsvHelperV2([first, second], RunDate).ConvertToSDSCSV();

        Assert.Equal(2, v1.Teachers.Count(row => row.Username == "shared@example.test"));
        Assert.Equal(2, v2.Users.Count(row => row.username == "shared@example.test"));
    }

    [Fact]
    public void ExactDuplicatePopulationRowsAreEmittedOnce()
    {
        Medewerker teacher = Teacher(Guid.NewGuid(), "teacher@example.test");
        Leerling student = Student(Guid.NewGuid(), "student@example.test");
        OuderVerzorger guardian = Guardian(Guid.NewGuid(), "guardian@example.test", student.Uuid);
        ResolvedExportPopulation population = Population("A", "A-Class", teacher, student, guardian);

        SDScsvV1 v1 = new SDScsvHelperV1([population, population], RunDate).ConvertToSDSCSV();
        SDScsvV2 v2 = new SDScsvHelperV2([population, population], RunDate).ConvertToSDSCSV();

        Assert.Single(v1.Schools);
        Assert.Single(v1.Sections);
        Assert.Single(v1.Teachers);
        Assert.Single(v1.Students);
        Assert.Single(v1.TeacherRosters);
        Assert.Single(v1.StudentEnrollments);
        Assert.Single(v1.User);
        Assert.Single(v1.Guardianrelationship);

        Assert.Single(v2.Orgs);
        Assert.Single(v2.Classes);
        Assert.Equal(3, v2.Users.Count);
        Assert.Equal(3, v2.Roles.Count);
        Assert.Equal(2, v2.Enrollments.Count);
        Assert.Single(v2.Relationships);
    }

    [Fact]
    public void DifferentClassesWithSameCurrentIdentifierBlockBothVersions()
    {
        ResolvedExportPopulation first = Population(
            "A",
            "AX",
            Teacher(Guid.NewGuid(), "first-teacher@example.test"),
            Student(Guid.NewGuid(), "first-student@example.test"));
        ResolvedExportPopulation second = Population(
            "a",
            "ax",
            Teacher(Guid.NewGuid(), "second-teacher@example.test"),
            Student(Guid.NewGuid(), "second-student@example.test"));

        Assert.Throws<InvalidOperationException>(() =>
            new SDScsvHelperV1([first, second], RunDate).ConvertToSDSCSV());
        Assert.Throws<InvalidOperationException>(() =>
            new SDScsvHelperV2([first, second], RunDate).ConvertToSDSCSV());
    }

    [Fact]
    public void ClassCollisionBlocksOnlyTheAffectedSdsVersion()
    {
        ResolvedExportPopulation v1First = Population(
            "A_",
            "A B",
            Teacher(Guid.NewGuid(), "first-teacher@example.test"),
            Student(Guid.NewGuid(), "first-student@example.test"));
        ResolvedExportPopulation v1Second = Population(
            "a_",
            "a_A_B",
            Teacher(Guid.NewGuid(), "second-teacher@example.test"),
            Student(Guid.NewGuid(), "second-student@example.test"));

        Assert.Throws<InvalidOperationException>(() =>
            new SDScsvHelperV1([v1First, v1Second], RunDate).ConvertToSDSCSV());
        Assert.Equal(
            2,
            new SDScsvHelperV2([v1First, v1Second], RunDate).ConvertToSDSCSV().Classes.Count);
    }

    private static ResolvedExportPopulation Population(
        string locationAbbreviation,
        string className,
        Medewerker teacher,
        Leerling student,
        OuderVerzorger guardian = null)
    {
        Lesgroep sourceClass = new()
        {
            Uuid = Guid.NewGuid(),
            Naam = className,
            Vaknaam = "Subject",
            Onderwijssoort = "Education"
        };
        IReadOnlyList<ResolvedGuardian> guardians = guardian is null
            ? []
            : [new ResolvedGuardian(guardian, [student.Uuid])];

        return new ResolvedExportPopulation(
            new Vestiging
            {
                Uuid = Guid.NewGuid(),
                Naam = $"Location {locationAbbreviation}",
                Afkorting = locationAbbreviation
            },
            [new ResolvedClass(sourceClass, [teacher], [student])],
            [teacher],
            [student],
            guardians);
    }

    private static Medewerker Teacher(Guid uuid, string email)
    {
        return new Medewerker { Uuid = uuid, Emailadres = email };
    }

    private static Leerling Student(Guid uuid, string email)
    {
        return new Leerling { Uuid = uuid, Emailadres = email };
    }

    private static OuderVerzorger Guardian(Guid uuid, string email, Guid studentUuid)
    {
        return new OuderVerzorger
        {
            Uuid = uuid,
            Emailadres = email,
            WenstContactViaEMail = true,
            Voorletters = "G.",
            Voorvoegsel = string.Empty,
            Achternaam = "Guardian",
            Telefoonnummer = string.Empty,
            UitgebreidMobielNummer = string.Empty,
            UitgebreidTelefoonnummer = string.Empty,
            UitgebreidMobielWerkNummer = string.Empty,
            Leerlingen_van_vestiging = [studentUuid]
        };
    }
}

using System.Reflection;
using System.Text;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Somtoday2MicrosoftSDS.Helpers;
using Somtoday2MicrosoftSDS.Models;
using Xunit;

namespace Somtoday2MicrosoftSDS.Tests;

public sealed class CsvWireValidationTests
{
    private static readonly string[] LineBreaks = ["\r", "\n", "\r\n"];

    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void V1MappedLineBreakIsRejectedWithoutSourceDataInError(string lineBreak)
    {
        const string sensitiveValue = "private-source-value";
        SDScsvV1 csv = new();
        csv.Schools.Add(new School
        {
            SISid = "private-source-id",
            Name = sensitiveValue + lineBreak
        });

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new FileHelper().CreateV1Dataset(csv, includeGuardianSync: false));

        Assert.Equal(
            "SDS V1 file 'School.csv' column 'Name' contains a prohibited CR or LF character",
            exception.Message);
        Assert.DoesNotContain(sensitiveValue, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-source-id", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void V21MappedLineBreakIsRejectedWithExactColumnContext(string lineBreak)
    {
        SDScsvV2 csv = new();
        csv.Users.Add(new Users
        {
            sourcedId = "safe-id",
            username = "user" + lineBreak,
            givenName = string.Empty,
            familyName = string.Empty
        });

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new FileHelper().CreateV2Dataset(csv, includeGuardianSync: false));

        Assert.Equal(
            "SDS V2.1 file 'users.csv' column 'username' contains a prohibited CR or LF character",
            exception.Message);
    }

    [Fact]
    public void UnmappedTeacherNameFieldsDoNotChangeTheV1WireShape()
    {
        SDScsvV1 csv = new();
        csv.Teachers.Add(new Teacher
        {
            SISid = "teacher-id",
            SISSchoolid = "school-id",
            Username = "teacher@example.test",
            Firstname = "not\nserialized",
            Lastname = "not\rserialized"
        });

        PublicationDataset dataset = new FileHelper().CreateV1Dataset(csv, includeGuardianSync: false);
        string content = Assert.Single(dataset.Files, file => file.Name == "Teacher.csv").Content.ToString();

        Assert.Equal(
            "SIS ID,School SIS ID,Username\r\nteacher-id,school-id,teacher@example.test\r\n",
            content);
    }

    [Fact]
    public void HeaderOnlyDatasetsHaveExactNamesHeadersAndUtf8WithoutBom()
    {
        FileHelper helper = new();

        AssertDataset(
            helper.CreateEmptyV1Dataset(includeGuardianSync: true),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["School.csv"] = "SIS ID,Name",
                ["Section.csv"] = "SIS ID,School SIS ID,Section Name,Section Number,Course Name,Course Description",
                ["Teacher.csv"] = "SIS ID,School SIS ID,Username",
                ["Student.csv"] = "SIS ID,School SIS ID,Username",
                ["TeacherRoster.csv"] = "Section SIS ID,SIS ID",
                ["StudentEnrollment.csv"] = "Section SIS ID,SIS ID",
                ["User.csv"] = "Email,First Name,Last Name,Phone,SIS ID",
                ["Guardianrelationship.csv"] = "SIS ID,Email,Role"
            });
        AssertDataset(
            helper.CreateEmptyV2Dataset(includeGuardianSync: true),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["orgs.csv"] = "sourcedId,name,type,parentSourcedId",
                ["users.csv"] = "sourcedId,username,givenName,familyName,password,activeDirectoryMatchId,email,phone,sms",
                ["roles.csv"] = "userSourcedId,orgSourcedId,role",
                ["classes.csv"] = "sourcedId,orgSourcedId,title,sessionSourcedIds,courseSourcedId",
                ["enrollments.csv"] = "classSourcedId,userSourcedId,role",
                ["relationships.csv"] = "userSourcedId,relationshipUserSourcedId,relationshipRole"
            });
    }

    [Fact]
    public void EveryWritableMappedStringColumnRejectsEveryLineBreakForm()
    {
        List<string> nonWritableMappedStrings = [];
        int validatedFields = 0;

        foreach (WireMapCase wireMap in GetWireMaps())
        {
            ClassMap map = Assert.IsAssignableFrom<ClassMap>(Activator.CreateInstance(wireMap.MapType));
            foreach (MemberMap memberMap in map.MemberMaps.Where(member => member.Data.Type == typeof(string)))
            {
                if (memberMap.Data.Member is not PropertyInfo property || !property.CanWrite)
                {
                    nonWritableMappedStrings.Add($"{wireMap.FileName}:{memberMap.Data.Member.Name}");
                    continue;
                }

                string columnName = memberMap.Data.Names.FirstOrDefault() ?? property.Name;
                foreach (string lineBreak in LineBreaks)
                {
                    object record = Activator.CreateInstance(wireMap.RecordType);
                    property.SetValue(record, "private-source-value" + lineBreak);

                    InvalidDataException exception = Assert.Throws<InvalidDataException>(
                        () => wireMap.Serialize(record));

                    Assert.Equal(
                        $"SDS {wireMap.SdsVersion} file '{wireMap.FileName}' column '{columnName}' contains a prohibited CR or LF character",
                        exception.Message);
                    Assert.DoesNotContain("private-source-value", exception.Message, StringComparison.Ordinal);
                }

                validatedFields++;
            }
        }

        Assert.True(validatedFields > 0);
        Assert.Equal(["Guardianrelationship.csv:Role"], nonWritableMappedStrings);
    }

    [Fact]
    public async Task WireFailureHappensBeforeStagingAndDoesNotSuppressTheNextVersion()
    {
        Guid schoolUuid = Guid.NewGuid();
        HashSet<Guid> failedSchools = [];
        int stagingUploads = 0;
        int nextVersionAttempts = 0;
        string liveContent = "previous live content";
        SDScsvV1 invalidV1 = new();
        invalidV1.Schools.Add(new School { SISid = "private-id", Name = "invalid\nname" });

        await Program.PublishVersionAsync(
            "V1",
            "output/v1",
            [schoolUuid],
            failedSchools,
            () =>
            {
                _ = new FileHelper().CreateV1Dataset(invalidV1, includeGuardianSync: false);
                stagingUploads++;
                liveContent = "new live content";
                return Task.FromResult(DatasetPublicationResult.Succeeded);
            },
            NullLogger<Program>.Instance,
            CancellationToken.None);

        await Program.PublishVersionAsync(
            "V2.1",
            "output/v2",
            [schoolUuid],
            failedSchools,
            () =>
            {
                nextVersionAttempts++;
                return Task.FromResult(DatasetPublicationResult.Succeeded);
            },
            NullLogger<Program>.Instance,
            CancellationToken.None);

        Assert.Equal(0, stagingUploads);
        Assert.Equal("previous live content", liveContent);
        Assert.Contains(schoolUuid, failedSchools);
        Assert.Equal(1, nextVersionAttempts);
    }

    private static void AssertDataset(
        PublicationDataset dataset,
        IReadOnlyDictionary<string, string> expectedHeaders)
    {
        Assert.Equal(expectedHeaders.Keys, dataset.Files.Select(file => file.Name));

        foreach (PublicationFile file in dataset.Files)
        {
            ReadOnlySpan<byte> bytes = file.Content.ToMemory().Span;
            Assert.False(bytes.StartsWith(Encoding.UTF8.Preamble));
            Assert.Equal(expectedHeaders[file.Name] + "\r\n", file.Content.ToString());
        }
    }

    private static IReadOnlyList<WireMapCase> GetWireMaps()
    {
        return
        [
            new("V1", "School.csv", typeof(School), typeof(SchoolCSVMap), record =>
            {
                SDScsvV1 csv = new();
                csv.Schools.Add((School)record);
                _ = new FileHelper().CreateV1Dataset(csv, includeGuardianSync: false);
            }),
            new("V1", "Section.csv", typeof(Section), typeof(SectionCSVMap), record =>
            {
                SDScsvV1 csv = new();
                csv.Sections.Add((Section)record);
                _ = new FileHelper().CreateV1Dataset(csv, includeGuardianSync: false);
            }),
            new("V1", "Teacher.csv", typeof(Teacher), typeof(TeacherCSVMap), record =>
            {
                SDScsvV1 csv = new();
                csv.Teachers.Add((Teacher)record);
                _ = new FileHelper().CreateV1Dataset(csv, includeGuardianSync: false);
            }),
            new("V1", "Student.csv", typeof(Student), typeof(StudentCSVMap), record =>
            {
                SDScsvV1 csv = new();
                csv.Students.Add((Student)record);
                _ = new FileHelper().CreateV1Dataset(csv, includeGuardianSync: false);
            }),
            new("V1", "TeacherRoster.csv", typeof(TeacherRoster), typeof(TeacherRosterCSVMap), record =>
            {
                SDScsvV1 csv = new();
                csv.TeacherRosters.Add((TeacherRoster)record);
                _ = new FileHelper().CreateV1Dataset(csv, includeGuardianSync: false);
            }),
            new("V1", "StudentEnrollment.csv", typeof(StudentEnrollment), typeof(StudentEnrollmentCSVMap), record =>
            {
                SDScsvV1 csv = new();
                csv.StudentEnrollments.Add((StudentEnrollment)record);
                _ = new FileHelper().CreateV1Dataset(csv, includeGuardianSync: false);
            }),
            new("V1", "User.csv", typeof(Guardian), typeof(GuardianCSVMap), record =>
            {
                SDScsvV1 csv = new();
                csv.User.Add((Guardian)record);
                _ = new FileHelper().CreateV1Dataset(csv, includeGuardianSync: true);
            }),
            new("V1", "Guardianrelationship.csv", typeof(GuardianRelationship), typeof(GuardianRelationshipCSVMap), record =>
            {
                SDScsvV1 csv = new();
                csv.Guardianrelationship.Add((GuardianRelationship)record);
                _ = new FileHelper().CreateV1Dataset(csv, includeGuardianSync: true);
            }),
            new("V2.1", "orgs.csv", typeof(Orgs), typeof(OrgsClassMap), record =>
            {
                SDScsvV2 csv = new();
                csv.Orgs.Add((Orgs)record);
                _ = new FileHelper().CreateV2Dataset(csv, includeGuardianSync: false);
            }),
            new("V2.1", "users.csv", typeof(Users), typeof(UsersClassMap), record =>
            {
                SDScsvV2 csv = new();
                csv.Users.Add((Users)record);
                _ = new FileHelper().CreateV2Dataset(csv, includeGuardianSync: false);
            }),
            new("V2.1", "roles.csv", typeof(Roles), typeof(RolesClassMap), record =>
            {
                SDScsvV2 csv = new();
                csv.Roles.Add((Roles)record);
                _ = new FileHelper().CreateV2Dataset(csv, includeGuardianSync: false);
            }),
            new("V2.1", "classes.csv", typeof(Classes), typeof(ClassesClassMap), record =>
            {
                SDScsvV2 csv = new();
                csv.Classes.Add((Classes)record);
                _ = new FileHelper().CreateV2Dataset(csv, includeGuardianSync: false);
            }),
            new("V2.1", "enrollments.csv", typeof(Enrollments), typeof(EnrollmentsClassMap), record =>
            {
                SDScsvV2 csv = new();
                csv.Enrollments.Add((Enrollments)record);
                _ = new FileHelper().CreateV2Dataset(csv, includeGuardianSync: false);
            }),
            new("V2.1", "relationships.csv", typeof(Relationships), typeof(RelationshipsClassMap), record =>
            {
                SDScsvV2 csv = new();
                csv.Relationships.Add((Relationships)record);
                _ = new FileHelper().CreateV2Dataset(csv, includeGuardianSync: true);
            })
        ];
    }

    private sealed record WireMapCase(
        string SdsVersion,
        string FileName,
        Type RecordType,
        Type MapType,
        Action<object> Serialize);
}

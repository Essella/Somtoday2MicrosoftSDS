using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Somtoday2MicrosoftSDS.Models;

namespace Somtoday2MicrosoftSDS.Helpers
{
    internal class FileHelper
    {
        private static readonly string[] V1CoreFileNames =
        [
            "School.csv",
            "Section.csv",
            "Teacher.csv",
            "Student.csv",
            "TeacherRoster.csv",
            "StudentEnrollment.csv"
        ];

        private static readonly string[] V1GuardianFileNames =
        [
            "User.csv",
            "Guardianrelationship.csv"
        ];

        private static readonly string[] V2CoreFileNames =
        [
            "orgs.csv",
            "users.csv",
            "roles.csv",
            "classes.csv",
            "enrollments.csv"
        ];

        private static readonly string[] V2GuardianFileNames = ["relationships.csv"];

        private readonly CsvConfiguration _configuration = new(CultureInfo.InvariantCulture)
        {
            Delimiter = ",",
            Encoding = Encoding.UTF8
        };

        internal PublicationDataset CreateV1Dataset(SDScsvV1 sdsCsv, bool includeGuardianSync)
        {
            List<PublicationFile> files =
            [
                SerializeCsv<School, SchoolCSVMap>("School.csv", sdsCsv.Schools),
                SerializeCsv<Section, SectionCSVMap>("Section.csv", sdsCsv.Sections),
                SerializeCsv<Teacher, TeacherCSVMap>("Teacher.csv", sdsCsv.Teachers),
                SerializeCsv<Student, StudentCSVMap>("Student.csv", sdsCsv.Students),
                SerializeCsv<TeacherRoster, TeacherRosterCSVMap>("TeacherRoster.csv", sdsCsv.TeacherRosters),
                SerializeCsv<StudentEnrollment, StudentEnrollmentCSVMap>("StudentEnrollment.csv", sdsCsv.StudentEnrollments)
            ];

            if (includeGuardianSync)
            {
                files.Add(SerializeCsv<Guardian, GuardianCSVMap>("User.csv", sdsCsv.User));
                files.Add(SerializeCsv<GuardianRelationship, GuardianRelationshipCSVMap>(
                    "Guardianrelationship.csv",
                    sdsCsv.Guardianrelationship));
            }

            return new PublicationDataset(
                "v1",
                includeGuardianSync,
                files,
                V1CoreFileNames,
                V1GuardianFileNames);
        }

        internal PublicationDataset CreateEmptyV1Dataset(bool includeGuardianSync)
        {
            return CreateV1Dataset(new SDScsvV1(), includeGuardianSync);
        }

        internal PublicationDataset CreateV2Dataset(SDScsvV2 sdsCsv, bool includeGuardianSync)
        {
            List<PublicationFile> files =
            [
                SerializeCsv<Orgs, OrgsClassMap>("orgs.csv", sdsCsv.Orgs),
                SerializeCsv<Users, UsersClassMap>("users.csv", sdsCsv.Users),
                SerializeCsv<Roles, RolesClassMap>("roles.csv", sdsCsv.Roles),
                SerializeCsv<Classes, ClassesClassMap>("classes.csv", sdsCsv.Classes),
                SerializeCsv<Enrollments, EnrollmentsClassMap>("enrollments.csv", sdsCsv.Enrollments)
            ];

            if (includeGuardianSync)
            {
                files.Add(SerializeCsv<Relationships, RelationshipsClassMap>(
                    "relationships.csv",
                    sdsCsv.Relationships));
            }

            return new PublicationDataset(
                "v2",
                includeGuardianSync,
                files,
                V2CoreFileNames,
                V2GuardianFileNames);
        }

        internal PublicationDataset CreateEmptyV2Dataset(bool includeGuardianSync)
        {
            return CreateV2Dataset(new SDScsvV2(), includeGuardianSync);
        }

        private PublicationFile SerializeCsv<TRecord, TMap>(
            string fileName,
            IEnumerable<TRecord> records)
            where TMap : ClassMap<TRecord>
        {
            using MemoryStream stream = new();
            using (StreamWriter writer = new(stream, new UTF8Encoding(false), leaveOpen: true))
            using (CsvWriter csv = new(writer, _configuration))
            {
                csv.Context.RegisterClassMap<TMap>();
                csv.WriteHeader<TRecord>();
                csv.NextRecord();
                foreach (TRecord record in records)
                {
                    csv.WriteRecord(record);
                    csv.NextRecord();
                }
            }

            return new PublicationFile(fileName, BinaryData.FromBytes(stream.ToArray()));
        }
    }
}

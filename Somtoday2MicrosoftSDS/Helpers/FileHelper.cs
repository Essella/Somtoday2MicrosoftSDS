using System.Globalization;
using System.Reflection;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Somtoday2MicrosoftSDS.Models;

namespace Somtoday2MicrosoftSDS.Helpers
{
    internal class FileHelper
    {
        private readonly CsvConfiguration _configuration = new(CultureInfo.InvariantCulture)
        {
            Delimiter = ",",
            Encoding = Encoding.UTF8
        };

        internal PublicationDataset CreateV1Dataset(SDScsvV1 sdsCsv, bool includeGuardianSync)
        {
            List<PublicationFile> files =
            [
                SerializeCsv<School, SchoolCSVMap>("V1", "School.csv", sdsCsv.Schools),
                SerializeCsv<Section, SectionCSVMap>("V1", "Section.csv", sdsCsv.Sections),
                SerializeCsv<Teacher, TeacherCSVMap>("V1", "Teacher.csv", sdsCsv.Teachers),
                SerializeCsv<Student, StudentCSVMap>("V1", "Student.csv", sdsCsv.Students),
                SerializeCsv<TeacherRoster, TeacherRosterCSVMap>("V1", "TeacherRoster.csv", sdsCsv.TeacherRosters),
                SerializeCsv<StudentEnrollment, StudentEnrollmentCSVMap>("V1", "StudentEnrollment.csv", sdsCsv.StudentEnrollments)
            ];

            if (includeGuardianSync)
            {
                files.Add(SerializeCsv<Guardian, GuardianCSVMap>("V1", "User.csv", sdsCsv.User));
                files.Add(SerializeCsv<GuardianRelationship, GuardianRelationshipCSVMap>(
                    "V1",
                    "Guardianrelationship.csv",
                    sdsCsv.Guardianrelationship));
            }

            return new PublicationDataset(SdsDatasetFormat.V1, files, includeGuardianSync);
        }

        internal PublicationDataset CreateEmptyV1Dataset(bool includeGuardianSync)
        {
            return CreateV1Dataset(new SDScsvV1(), includeGuardianSync);
        }

        internal PublicationDataset CreateV2Dataset(SDScsvV2 sdsCsv, bool includeGuardianSync)
        {
            List<PublicationFile> files =
            [
                SerializeCsv<Orgs, OrgsClassMap>("V2.1", "orgs.csv", sdsCsv.Orgs),
                SerializeCsv<Users, UsersClassMap>("V2.1", "users.csv", sdsCsv.Users),
                SerializeCsv<Roles, RolesClassMap>("V2.1", "roles.csv", sdsCsv.Roles),
                SerializeCsv<Classes, ClassesClassMap>("V2.1", "classes.csv", sdsCsv.Classes),
                SerializeCsv<Enrollments, EnrollmentsClassMap>("V2.1", "enrollments.csv", sdsCsv.Enrollments)
            ];

            if (includeGuardianSync)
            {
                files.Add(SerializeCsv<Relationships, RelationshipsClassMap>(
                    "V2.1",
                    "relationships.csv",
                    sdsCsv.Relationships));
            }

            return new PublicationDataset(SdsDatasetFormat.V2Rev1, files, includeGuardianSync);
        }

        internal PublicationDataset CreateEmptyV2Dataset(bool includeGuardianSync)
        {
            return CreateV2Dataset(new SDScsvV2(), includeGuardianSync);
        }

        private PublicationFile SerializeCsv<TRecord, TMap>(
            string sdsVersion,
            string fileName,
            IEnumerable<TRecord> records)
            where TMap : ClassMap<TRecord>, new()
        {
            using MemoryStream stream = new();
            using (StreamWriter writer = new(stream, new UTF8Encoding(false), leaveOpen: true))
            using (CsvWriter csv = new(writer, _configuration))
            {
                TMap map = new();
                csv.Context.RegisterClassMap(map);
                csv.WriteHeader<TRecord>();
                csv.NextRecord();
                foreach (TRecord record in records)
                {
                    ValidateMappedStrings(sdsVersion, fileName, record, map);
                    csv.WriteRecord(record);
                    csv.NextRecord();
                }
            }

            return new PublicationFile(fileName, BinaryData.FromBytes(stream.ToArray()));
        }

        private static void ValidateMappedStrings<TRecord>(
            string sdsVersion,
            string fileName,
            TRecord record,
            ClassMap<TRecord> map)
        {
            foreach (MemberMap memberMap in map.MemberMaps)
            {
                if (memberMap.Data.Type != typeof(string))
                {
                    continue;
                }

                string value = memberMap.Data.Member switch
                {
                    PropertyInfo property => property.GetValue(record) as string,
                    FieldInfo field => field.GetValue(record) as string,
                    _ => null
                };

                if (string.IsNullOrEmpty(value) || value.IndexOfAny(['\r', '\n']) < 0)
                {
                    continue;
                }

                string columnName = memberMap.Data.Names.FirstOrDefault()
                    ?? memberMap.Data.Member.Name;
                throw new InvalidDataException(
                    $"SDS {sdsVersion} file '{fileName}' column '{columnName}' contains a prohibited CR or LF character");
            }
        }
    }
}

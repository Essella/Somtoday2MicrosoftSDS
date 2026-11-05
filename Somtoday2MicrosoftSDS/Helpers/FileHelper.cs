using System.Reflection;
using System.Text;
using CsvHelper;
using Somtoday2MicrosoftSDS.Models;

namespace Somtoday2MicrosoftSDS.Helpers
{
    internal class FileHelper
    {
        internal PublicationDataset CreateV1Dataset(SDScsvV1 sdsCsv, bool includeGuardianSync)
        {
            List<PublicationFile> files =
            [
                SerializeCsv<School>("V1", "School.csv", sdsCsv.Schools),
                SerializeCsv<Section>("V1", "Section.csv", sdsCsv.Sections),
                SerializeCsv<Teacher>("V1", "Teacher.csv", sdsCsv.Teachers),
                SerializeCsv<Student>("V1", "Student.csv", sdsCsv.Students),
                SerializeCsv<TeacherRoster>("V1", "TeacherRoster.csv", sdsCsv.TeacherRosters),
                SerializeCsv<StudentEnrollment>("V1", "StudentEnrollment.csv", sdsCsv.StudentEnrollments)
            ];

            if (includeGuardianSync)
            {
                files.Add(SerializeCsv<Guardian>("V1", "User.csv", sdsCsv.User));
                files.Add(SerializeCsv<GuardianRelationship>(
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
                SerializeCsv<Orgs>("V2.1", "orgs.csv", sdsCsv.Orgs),
                SerializeCsv<Users>("V2.1", "users.csv", sdsCsv.Users),
                SerializeCsv<Roles>("V2.1", "roles.csv", sdsCsv.Roles),
                SerializeCsv<Classes>("V2.1", "classes.csv", sdsCsv.Classes),
                SerializeCsv<Enrollments>("V2.1", "enrollments.csv", sdsCsv.Enrollments)
            ];

            if (includeGuardianSync)
            {
                files.Add(SerializeCsv<Relationships>(
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

        private PublicationFile SerializeCsv<TRecord>(
            string sdsVersion,
            string fileName,
            IEnumerable<TRecord> records)
        {
            using MemoryStream stream = new();
            using (StreamWriter writer = new(stream, new UTF8Encoding(false), leaveOpen: true))
            using (CsvWriter csv = CsvWriter.Create(writer, leaveOpen: true))
            {
                csv.WriteHeader<TRecord>();
                foreach (TRecord record in records)
                {
                    ValidateMappedStrings(sdsVersion, fileName, record);
                    csv.WriteRecord(record);
                }
            }

            return new PublicationFile(fileName, BinaryData.FromBytes(stream.ToArray()));
        }

        private static void ValidateMappedStrings<TRecord>(
            string sdsVersion,
            string fileName,
            TRecord record)
        {
            foreach (PropertyInfo property in typeof(TRecord).GetProperties())
            {
                CsvColumnAttribute column = property.GetCustomAttribute<CsvColumnAttribute>();
                if (column == null || property.PropertyType != typeof(string))
                {
                    continue;
                }

                string value = property.GetValue(record) as string;

                if (string.IsNullOrEmpty(value) || value.IndexOfAny(['\r', '\n']) < 0)
                {
                    continue;
                }

                string columnName = column.Name ?? property.Name;
                throw new InvalidDataException(
                    $"SDS {sdsVersion} file '{fileName}' column '{columnName}' contains a prohibited CR or LF character");
            }
        }
    }
}

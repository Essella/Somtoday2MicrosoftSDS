using System.Globalization;
using System.Text;
using Azure.Storage.Blobs;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using SyncIdPreview.Models;

namespace SyncIdPreview.Helpers
{
    internal class FileHelper
    {
        private readonly ILogger<FileHelper> _logger;

        private readonly CsvConfiguration config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ",",
            Encoding = Encoding.UTF8
        };

        public FileHelper(ILogger<FileHelper> logger = null)
        {
            _logger = logger ?? LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<FileHelper>();
        }

        internal void SaveJsonToDisk(List<VestigingModel> allInfo, string targetFolder)
        {
            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(allInfo);
                string filePath = Path.Combine(targetFolder, "allInfo.json");
                File.WriteAllText(filePath, json);
                _logger?.LogInformation("JSON saved to: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error saving JSON to disk");
                throw;
            }
        }

        internal List<VestigingModel> LoadJsonFromDisk(string targetFolder)
        {
            try
            {
                string filePath = Path.Combine(targetFolder, "allInfo.json");
                string json = File.ReadAllText(filePath);
                List<VestigingModel> allInfo = System.Text.Json.JsonSerializer.Deserialize<List<VestigingModel>>(json);
                _logger?.LogInformation("JSON loaded from: {FilePath}", filePath);
                return allInfo;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading JSON from disk");
                throw;
            }
        }

        internal void SaveV1ToDisk(List<SDScsvV1> sdsCsv, string actualOutputFolder)
        {
            SaveV1ToDisk(CombineV1(sdsCsv), actualOutputFolder);
        }

        internal void SaveV1ToDisk(SDScsvV1 sdsCsv, string actualOutputFolder)
        {
            try
            {
                CreateOutputFolderIfNeeded(actualOutputFolder);

                WriteCsv<School, SchoolCSVMap>(Path.Combine(actualOutputFolder, "School.csv"), sdsCsv.Schools);
                WriteCsv<Section, SectionCSVMap>(Path.Combine(actualOutputFolder, "Section.csv"), sdsCsv.Sections);
                WriteCsv<Teacher, TeacherCSVMap>(Path.Combine(actualOutputFolder, "Teacher.csv"), sdsCsv.Teachers);
                WriteCsv<Student, StudentCSVMap>(Path.Combine(actualOutputFolder, "Student.csv"), sdsCsv.Students);
                WriteCsv<TeacherRoster, TeacherRosterCSVMap>(Path.Combine(actualOutputFolder, "TeacherRoster.csv"), sdsCsv.TeacherRosters);
                WriteCsv<StudentEnrollment, StudentEnrollmentCSVMap>(Path.Combine(actualOutputFolder, "StudentEnrollment.csv"), sdsCsv.StudentEnrollments);

                if (sdsCsv.User.Count > 0)
                {
                    WriteCsv<Guardian, GuardianCSVMap>(Path.Combine(actualOutputFolder, "User.csv"), sdsCsv.User);
                    WriteCsv<GuardianRelationship, GuardianRelationshipCSVMap>(Path.Combine(actualOutputFolder, "Guardianrelationship.csv"), sdsCsv.Guardianrelationship);
                }

                _logger?.LogInformation("V1 CSV files saved to: {OutputFolder}", actualOutputFolder);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error saving V1 CSV to disk");
                throw;
            }
        }

        internal void SaveEmptyV1ToDisk(string outputFolder, bool includeGuardianSync)
        {
            try
            {
                CreateOutputFolderIfNeeded(outputFolder);

                SDScsvV1 emptyCsv = new SDScsvV1();
                WriteCsv<School, SchoolCSVMap>(Path.Combine(outputFolder, "School.csv"), emptyCsv.Schools);
                WriteCsv<Section, SectionCSVMap>(Path.Combine(outputFolder, "Section.csv"), emptyCsv.Sections);
                WriteCsv<Teacher, TeacherCSVMap>(Path.Combine(outputFolder, "Teacher.csv"), emptyCsv.Teachers);
                WriteCsv<Student, StudentCSVMap>(Path.Combine(outputFolder, "Student.csv"), emptyCsv.Students);
                WriteCsv<TeacherRoster, TeacherRosterCSVMap>(Path.Combine(outputFolder, "TeacherRoster.csv"), emptyCsv.TeacherRosters);
                WriteCsv<StudentEnrollment, StudentEnrollmentCSVMap>(Path.Combine(outputFolder, "StudentEnrollment.csv"), emptyCsv.StudentEnrollments);

                if (includeGuardianSync)
                {
                    WriteCsv<Guardian, GuardianCSVMap>(Path.Combine(outputFolder, "User.csv"), emptyCsv.User);
                    WriteCsv<GuardianRelationship, GuardianRelationshipCSVMap>(Path.Combine(outputFolder, "Guardianrelationship.csv"), emptyCsv.Guardianrelationship);
                }

                _logger?.LogInformation("Empty V1 CSV files saved to: {OutputFolder}", outputFolder);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error saving empty V1 CSV to disk");
                throw;
            }
        }

        internal void SaveV2ToDisk(List<SDScsvV2> sdsCsvList, string outputFolder)
        {
            SaveV2ToDisk(CombineV2(sdsCsvList), outputFolder);
        }

        internal void SaveV2ToDisk(SDScsvV2 sdsCsv, string outputFolder)
        {
            try
            {
                CreateOutputFolderIfNeeded(outputFolder);

                WriteCsv<Orgs, OrgsClassMap>(Path.Combine(outputFolder, "orgs.csv"), sdsCsv.Orgs);
                WriteCsv<Users, UsersClassMap>(Path.Combine(outputFolder, "users.csv"), sdsCsv.Users);
                WriteCsv<Roles, RolesClassMap>(Path.Combine(outputFolder, "roles.csv"), sdsCsv.Roles);
                WriteCsv<Classes, ClassesClassMap>(Path.Combine(outputFolder, "classes.csv"), sdsCsv.Classes);
                WriteCsv<Enrollments, EnrollmentsClassMap>(Path.Combine(outputFolder, "enrollments.csv"), sdsCsv.Enrollments);

                if (sdsCsv.Relationships.Count > 0)
                {
                    WriteCsv<Relationships, RelationshipsClassMap>(Path.Combine(outputFolder, "relationships.csv"), sdsCsv.Relationships);
                }

                _logger?.LogInformation("V2 CSV files saved to: {OutputFolder}", outputFolder);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error saving V2 CSV to disk");
                throw;
            }
        }

        internal void SaveEmptyV2ToDisk(string outputFolder, bool includeGuardianSync)
        {
            try
            {
                CreateOutputFolderIfNeeded(outputFolder);

                SDScsvV2 emptyCsv = new SDScsvV2();
                WriteCsv<Orgs, OrgsClassMap>(Path.Combine(outputFolder, "orgs.csv"), emptyCsv.Orgs);
                WriteCsv<Users, UsersClassMap>(Path.Combine(outputFolder, "users.csv"), emptyCsv.Users);
                WriteCsv<Roles, RolesClassMap>(Path.Combine(outputFolder, "roles.csv"), emptyCsv.Roles);
                WriteCsv<Classes, ClassesClassMap>(Path.Combine(outputFolder, "classes.csv"), emptyCsv.Classes);
                WriteCsv<Enrollments, EnrollmentsClassMap>(Path.Combine(outputFolder, "enrollments.csv"), emptyCsv.Enrollments);

                if (includeGuardianSync)
                {
                    WriteCsv<Relationships, RelationshipsClassMap>(Path.Combine(outputFolder, "relationships.csv"), emptyCsv.Relationships);
                }

                _logger?.LogInformation("Empty V2 CSV files saved to: {OutputFolder}", outputFolder);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error saving empty V2 CSV to disk");
                throw;
            }
        }

        internal async Task SaveV1ToBlobAsync(List<SDScsvV1> sdsCsv, BlobContainerClient containerClient, string blobPrefix)
        {
            await SaveV1ToBlobAsync(CombineV1(sdsCsv), containerClient, blobPrefix);
        }

        internal async Task SaveV1ToBlobAsync(SDScsvV1 sdsCsv, BlobContainerClient containerClient, string blobPrefix)
        {
            try
            {
                await UploadCsvAsync<School, SchoolCSVMap>(containerClient, CombineBlobPath(blobPrefix, "School.csv"), sdsCsv.Schools);
                await UploadCsvAsync<Section, SectionCSVMap>(containerClient, CombineBlobPath(blobPrefix, "Section.csv"), sdsCsv.Sections);
                await UploadCsvAsync<Teacher, TeacherCSVMap>(containerClient, CombineBlobPath(blobPrefix, "Teacher.csv"), sdsCsv.Teachers);
                await UploadCsvAsync<Student, StudentCSVMap>(containerClient, CombineBlobPath(blobPrefix, "Student.csv"), sdsCsv.Students);
                await UploadCsvAsync<TeacherRoster, TeacherRosterCSVMap>(containerClient, CombineBlobPath(blobPrefix, "TeacherRoster.csv"), sdsCsv.TeacherRosters);
                await UploadCsvAsync<StudentEnrollment, StudentEnrollmentCSVMap>(containerClient, CombineBlobPath(blobPrefix, "StudentEnrollment.csv"), sdsCsv.StudentEnrollments);

                if (sdsCsv.User.Count > 0)
                {
                    await UploadCsvAsync<Guardian, GuardianCSVMap>(containerClient, CombineBlobPath(blobPrefix, "User.csv"), sdsCsv.User);
                    await UploadCsvAsync<GuardianRelationship, GuardianRelationshipCSVMap>(containerClient, CombineBlobPath(blobPrefix, "Guardianrelationship.csv"), sdsCsv.Guardianrelationship);
                }

                _logger?.LogInformation("V1 CSV files uploaded to blob prefix: {BlobPrefix}", blobPrefix);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error uploading V1 CSV files to Azure Blob Storage");
                throw;
            }
        }

        internal async Task SaveEmptyV1ToBlobAsync(BlobContainerClient containerClient, string blobPrefix, bool includeGuardianSync)
        {
            try
            {
                SDScsvV1 emptyCsv = new SDScsvV1();
                await UploadCsvAsync<School, SchoolCSVMap>(containerClient, CombineBlobPath(blobPrefix, "School.csv"), emptyCsv.Schools);
                await UploadCsvAsync<Section, SectionCSVMap>(containerClient, CombineBlobPath(blobPrefix, "Section.csv"), emptyCsv.Sections);
                await UploadCsvAsync<Teacher, TeacherCSVMap>(containerClient, CombineBlobPath(blobPrefix, "Teacher.csv"), emptyCsv.Teachers);
                await UploadCsvAsync<Student, StudentCSVMap>(containerClient, CombineBlobPath(blobPrefix, "Student.csv"), emptyCsv.Students);
                await UploadCsvAsync<TeacherRoster, TeacherRosterCSVMap>(containerClient, CombineBlobPath(blobPrefix, "TeacherRoster.csv"), emptyCsv.TeacherRosters);
                await UploadCsvAsync<StudentEnrollment, StudentEnrollmentCSVMap>(containerClient, CombineBlobPath(blobPrefix, "StudentEnrollment.csv"), emptyCsv.StudentEnrollments);

                if (includeGuardianSync)
                {
                    await UploadCsvAsync<Guardian, GuardianCSVMap>(containerClient, CombineBlobPath(blobPrefix, "User.csv"), emptyCsv.User);
                    await UploadCsvAsync<GuardianRelationship, GuardianRelationshipCSVMap>(containerClient, CombineBlobPath(blobPrefix, "Guardianrelationship.csv"), emptyCsv.Guardianrelationship);
                }

                _logger?.LogInformation("Empty V1 CSV files uploaded to blob prefix: {BlobPrefix}", blobPrefix);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error uploading empty V1 CSV files to Azure Blob Storage");
                throw;
            }
        }

        internal async Task SaveV2ToBlobAsync(List<SDScsvV2> sdsCsvList, BlobContainerClient containerClient, string blobPrefix)
        {
            await SaveV2ToBlobAsync(CombineV2(sdsCsvList), containerClient, blobPrefix);
        }

        internal async Task SaveV2ToBlobAsync(SDScsvV2 sdsCsv, BlobContainerClient containerClient, string blobPrefix)
        {
            try
            {
                await UploadCsvAsync<Orgs, OrgsClassMap>(containerClient, CombineBlobPath(blobPrefix, "orgs.csv"), sdsCsv.Orgs);
                await UploadCsvAsync<Users, UsersClassMap>(containerClient, CombineBlobPath(blobPrefix, "users.csv"), sdsCsv.Users);
                await UploadCsvAsync<Roles, RolesClassMap>(containerClient, CombineBlobPath(blobPrefix, "roles.csv"), sdsCsv.Roles);
                await UploadCsvAsync<Classes, ClassesClassMap>(containerClient, CombineBlobPath(blobPrefix, "classes.csv"), sdsCsv.Classes);
                await UploadCsvAsync<Enrollments, EnrollmentsClassMap>(containerClient, CombineBlobPath(blobPrefix, "enrollments.csv"), sdsCsv.Enrollments);

                if (sdsCsv.Relationships.Count > 0)
                {
                    await UploadCsvAsync<Relationships, RelationshipsClassMap>(containerClient, CombineBlobPath(blobPrefix, "relationships.csv"), sdsCsv.Relationships);
                }

                _logger?.LogInformation("V2 CSV files uploaded to blob prefix: {BlobPrefix}", blobPrefix);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error uploading V2 CSV files to Azure Blob Storage");
                throw;
            }
        }

        internal async Task SaveEmptyV2ToBlobAsync(BlobContainerClient containerClient, string blobPrefix, bool includeGuardianSync)
        {
            try
            {
                SDScsvV2 emptyCsv = new SDScsvV2();
                await UploadCsvAsync<Orgs, OrgsClassMap>(containerClient, CombineBlobPath(blobPrefix, "orgs.csv"), emptyCsv.Orgs);
                await UploadCsvAsync<Users, UsersClassMap>(containerClient, CombineBlobPath(blobPrefix, "users.csv"), emptyCsv.Users);
                await UploadCsvAsync<Roles, RolesClassMap>(containerClient, CombineBlobPath(blobPrefix, "roles.csv"), emptyCsv.Roles);
                await UploadCsvAsync<Classes, ClassesClassMap>(containerClient, CombineBlobPath(blobPrefix, "classes.csv"), emptyCsv.Classes);
                await UploadCsvAsync<Enrollments, EnrollmentsClassMap>(containerClient, CombineBlobPath(blobPrefix, "enrollments.csv"), emptyCsv.Enrollments);

                if (includeGuardianSync)
                {
                    await UploadCsvAsync<Relationships, RelationshipsClassMap>(containerClient, CombineBlobPath(blobPrefix, "relationships.csv"), emptyCsv.Relationships);
                }

                _logger?.LogInformation("Empty V2 CSV files uploaded to blob prefix: {BlobPrefix}", blobPrefix);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error uploading empty V2 CSV files to Azure Blob Storage");
                throw;
            }
        }

        internal async Task SaveJsonToBlobAsync(List<VestigingModel> allInfo, BlobContainerClient containerClient)
        {
            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(allInfo);
                using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
                await containerClient.GetBlobClient("sds/temp/allInfo.json").UploadAsync(stream, overwrite: true);
                _logger?.LogInformation("JSON uploaded to blob prefix: sds/temp");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error uploading JSON to Azure Blob Storage");
                throw;
            }
        }

        internal async Task EnsureBlobStructureAsync(BlobContainerClient containerClient)
        {
            await containerClient.CreateIfNotExistsAsync();

            string[] prefixes =
            [
                "config/.keep",
                "logs/.keep",
                "sds/temp/.keep"
            ];

            foreach (string prefix in prefixes)
            {
                using MemoryStream stream = new MemoryStream(Array.Empty<byte>());
                await containerClient.GetBlobClient(prefix).UploadAsync(stream, overwrite: true);
            }
        }

        private void WriteCsv<TRecord, TMap>(string filePath, IEnumerable<TRecord> records)
            where TMap : ClassMap<TRecord>
        {
            using TextWriter writer = new StreamWriter(filePath);
            using CsvWriter csv = new CsvWriter(writer, config);
            csv.Context.RegisterClassMap<TMap>();
            csv.WriteHeader<TRecord>();
            csv.NextRecord();
            foreach (TRecord record in records)
            {
                csv.WriteRecord(record);
                csv.NextRecord();
            }
        }

        private async Task UploadCsvAsync<TRecord, TMap>(BlobContainerClient containerClient, string blobName, IEnumerable<TRecord> records)
            where TMap : ClassMap<TRecord>
        {
            using MemoryStream stream = new MemoryStream();
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true))
            using (CsvWriter csv = new CsvWriter(writer, config))
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

            stream.Position = 0;
            await containerClient.GetBlobClient(blobName).UploadAsync(stream, overwrite: true);
        }

        private static SDScsvV1 CombineV1(IEnumerable<SDScsvV1> sdsCsv)
        {
            return new SDScsvV1()
            {
                Schools = sdsCsv.SelectMany(o => o.Schools).ToList(),
                Sections = sdsCsv.SelectMany(o => o.Sections).ToList(),
                Teachers = sdsCsv.SelectMany(o => o.Teachers).ToList(),
                Students = sdsCsv.SelectMany(o => o.Students).ToList(),
                TeacherRosters = sdsCsv.SelectMany(o => o.TeacherRosters).ToList(),
                StudentEnrollments = sdsCsv.SelectMany(o => o.StudentEnrollments).ToList(),
                User = sdsCsv.SelectMany(o => o.User).ToList(),
                Guardianrelationship = sdsCsv.SelectMany(o => o.Guardianrelationship).ToList()
            };
        }

        private static SDScsvV2 CombineV2(IEnumerable<SDScsvV2> sdsCsvList)
        {
            return new SDScsvV2()
            {
                Orgs = sdsCsvList.SelectMany(o => o.Orgs).ToList(),
                Classes = sdsCsvList.SelectMany(c => c.Classes).ToList(),
                Enrollments = sdsCsvList.SelectMany(e => e.Enrollments).ToList(),
                Relationships = sdsCsvList.SelectMany(r => r.Relationships).ToList(),
                Roles = sdsCsvList.SelectMany(r => r.Roles).ToList(),
                Users = sdsCsvList.SelectMany(u => u.Users).ToList(),
            };
        }

        private static string CombineBlobPath(string prefix, string fileName)
        {
            return $"{prefix.Trim('/')}/{fileName}";
        }

        private void CreateOutputFolderIfNeeded(string outputFolder)
        {
            try
            {
                if (!Directory.Exists(outputFolder))
                {
                    _logger?.LogInformation("Output directory does not exist, creating: {OutputFolder}", outputFolder);
                    Directory.CreateDirectory(outputFolder);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error creating output folder: {OutputFolder}", outputFolder);
                throw;
            }
        }

        internal void ClearCsvFiles(string outputFolder, bool seperateOutputFolderForEachLocation)
        {
            try
            {
                if (seperateOutputFolderForEachLocation)
                {
                    string[] folders = Directory.GetDirectories(outputFolder);
                    foreach (string folder in folders)
                    {
                        string[] csvFiles = Directory.GetFiles(folder, "*.csv");
                        foreach (string file in csvFiles)
                        {
                            File.Delete(file);
                        }
                        _logger?.LogInformation("Cleared CSV files from: {Folder}", folder);
                    }
                }
                else
                {
                    string[] files = Directory.GetFiles(outputFolder, "*.csv");
                    foreach (string file in files)
                    {
                        File.Delete(file);
                    }
                    _logger?.LogInformation("Cleared CSV files from: {OutputFolder}", outputFolder);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error clearing CSV files");
                throw;
            }
        }
    }
}

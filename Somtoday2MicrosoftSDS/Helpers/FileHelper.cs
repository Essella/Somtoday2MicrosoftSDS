using System.Globalization;
using System.Text;
using Azure.Storage.Blobs;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Somtoday2MicrosoftSDS.Models;

namespace Somtoday2MicrosoftSDS.Helpers
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

        internal async Task SaveV1ToBlobAsync(
            SDScsvV1 sdsCsv,
            BlobContainerClient containerClient,
            string blobPrefix,
            CancellationToken cancellationToken)
        {
            try
            {
                await UploadCsvAsync<School, SchoolCSVMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "School.csv"), sdsCsv.Schools, cancellationToken);
                await UploadCsvAsync<Section, SectionCSVMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "Section.csv"), sdsCsv.Sections, cancellationToken);
                await UploadCsvAsync<Teacher, TeacherCSVMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "Teacher.csv"), sdsCsv.Teachers, cancellationToken);
                await UploadCsvAsync<Student, StudentCSVMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "Student.csv"), sdsCsv.Students, cancellationToken);
                await UploadCsvAsync<TeacherRoster, TeacherRosterCSVMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "TeacherRoster.csv"), sdsCsv.TeacherRosters, cancellationToken);
                await UploadCsvAsync<StudentEnrollment, StudentEnrollmentCSVMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "StudentEnrollment.csv"), sdsCsv.StudentEnrollments, cancellationToken);

                if (sdsCsv.User.Count > 0)
                {
                    await UploadCsvAsync<Guardian, GuardianCSVMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "User.csv"), sdsCsv.User, cancellationToken);
                    await UploadCsvAsync<GuardianRelationship, GuardianRelationshipCSVMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "Guardianrelationship.csv"), sdsCsv.Guardianrelationship, cancellationToken);
                }

                _logger?.LogInformation("V1 CSV files uploaded to blob prefix: {BlobPrefix}", blobPrefix);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    "Error uploading V1 CSV files to Azure Blob Storage ({Error})",
                    SafeExceptionSummary.Create(ex));
                throw;
            }
        }

        internal async Task SaveEmptyV1ToBlobAsync(
            BlobContainerClient containerClient,
            string blobPrefix,
            bool includeGuardianSync,
            CancellationToken cancellationToken)
        {
            try
            {
                SDScsvV1 emptyCsv = new SDScsvV1();
                await UploadCsvAsync<School, SchoolCSVMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "School.csv"), emptyCsv.Schools, cancellationToken);
                await UploadCsvAsync<Section, SectionCSVMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "Section.csv"), emptyCsv.Sections, cancellationToken);
                await UploadCsvAsync<Teacher, TeacherCSVMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "Teacher.csv"), emptyCsv.Teachers, cancellationToken);
                await UploadCsvAsync<Student, StudentCSVMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "Student.csv"), emptyCsv.Students, cancellationToken);
                await UploadCsvAsync<TeacherRoster, TeacherRosterCSVMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "TeacherRoster.csv"), emptyCsv.TeacherRosters, cancellationToken);
                await UploadCsvAsync<StudentEnrollment, StudentEnrollmentCSVMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "StudentEnrollment.csv"), emptyCsv.StudentEnrollments, cancellationToken);

                if (includeGuardianSync)
                {
                    await UploadCsvAsync<Guardian, GuardianCSVMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "User.csv"), emptyCsv.User, cancellationToken);
                    await UploadCsvAsync<GuardianRelationship, GuardianRelationshipCSVMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "Guardianrelationship.csv"), emptyCsv.Guardianrelationship, cancellationToken);
                }

                _logger?.LogInformation("Empty V1 CSV files uploaded to blob prefix: {BlobPrefix}", blobPrefix);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    "Error uploading empty V1 CSV files to Azure Blob Storage ({Error})",
                    SafeExceptionSummary.Create(ex));
                throw;
            }
        }

        internal async Task SaveV2ToBlobAsync(
            SDScsvV2 sdsCsv,
            BlobContainerClient containerClient,
            string blobPrefix,
            CancellationToken cancellationToken)
        {
            try
            {
                await UploadCsvAsync<Orgs, OrgsClassMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "orgs.csv"), sdsCsv.Orgs, cancellationToken);
                await UploadCsvAsync<Users, UsersClassMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "users.csv"), sdsCsv.Users, cancellationToken);
                await UploadCsvAsync<Roles, RolesClassMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "roles.csv"), sdsCsv.Roles, cancellationToken);
                await UploadCsvAsync<Classes, ClassesClassMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "classes.csv"), sdsCsv.Classes, cancellationToken);
                await UploadCsvAsync<Enrollments, EnrollmentsClassMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "enrollments.csv"), sdsCsv.Enrollments, cancellationToken);

                if (sdsCsv.Relationships.Count > 0)
                {
                    await UploadCsvAsync<Relationships, RelationshipsClassMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "relationships.csv"), sdsCsv.Relationships, cancellationToken);
                }

                _logger?.LogInformation("V2 CSV files uploaded to blob prefix: {BlobPrefix}", blobPrefix);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    "Error uploading V2 CSV files to Azure Blob Storage ({Error})",
                    SafeExceptionSummary.Create(ex));
                throw;
            }
        }

        internal async Task SaveEmptyV2ToBlobAsync(
            BlobContainerClient containerClient,
            string blobPrefix,
            bool includeGuardianSync,
            CancellationToken cancellationToken)
        {
            try
            {
                SDScsvV2 emptyCsv = new SDScsvV2();
                await UploadCsvAsync<Orgs, OrgsClassMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "orgs.csv"), emptyCsv.Orgs, cancellationToken);
                await UploadCsvAsync<Users, UsersClassMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "users.csv"), emptyCsv.Users, cancellationToken);
                await UploadCsvAsync<Roles, RolesClassMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "roles.csv"), emptyCsv.Roles, cancellationToken);
                await UploadCsvAsync<Classes, ClassesClassMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "classes.csv"), emptyCsv.Classes, cancellationToken);
                await UploadCsvAsync<Enrollments, EnrollmentsClassMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "enrollments.csv"), emptyCsv.Enrollments, cancellationToken);

                if (includeGuardianSync)
                {
                    await UploadCsvAsync<Relationships, RelationshipsClassMap>(containerClient, BlobPathHelper.Combine(blobPrefix, "relationships.csv"), emptyCsv.Relationships, cancellationToken);
                }

                _logger?.LogInformation("Empty V2 CSV files uploaded to blob prefix: {BlobPrefix}", blobPrefix);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    "Error uploading empty V2 CSV files to Azure Blob Storage ({Error})",
                    SafeExceptionSummary.Create(ex));
                throw;
            }
        }

        internal async Task EnsureContainerExistsAsync(
            BlobContainerClient containerClient,
            CancellationToken cancellationToken)
        {
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        }

        private async Task UploadCsvAsync<TRecord, TMap>(
            BlobContainerClient containerClient,
            string blobName,
            IEnumerable<TRecord> records,
            CancellationToken cancellationToken)
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
            await containerClient.GetBlobClient(blobName).UploadAsync(
                stream,
                overwrite: true,
                cancellationToken: cancellationToken);
        }
    }
}

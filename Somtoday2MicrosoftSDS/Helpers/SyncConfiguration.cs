using Microsoft.Extensions.Configuration;

namespace Somtoday2MicrosoftSDS.Helpers
{
    internal sealed record SyncConfiguration(
        Guid[] SchoolUuids,
        string ClientId,
        string ClientSecret,
        SomEnvironmentConfig SomEnvironment,
        string[] IncludedLocationCodes,
        string[] ExcludedLocationCodes,
        string BlobServiceUri,
        string BlobConnectionString,
        string BlobContainer,
        string OutputPrefix,
        bool GenerateEmptyCsv,
        bool EnableGuardianSync)
    {
        internal static bool TryCreate(
            IConfiguration configuration,
            string resolvedClientSecret,
            bool isDevelopment,
            out SyncConfiguration value,
            out string[] errors)
        {
            List<string> validationErrors = [];

            string[] configuredSchoolUuids = configuration.GetSection("Somtoday:SchoolUUID").Get<string[]>() ?? [];
            List<Guid> schoolUuids = [];
            HashSet<Guid> uniqueSchoolUuids = [];

            if (configuredSchoolUuids.Length == 0)
            {
                validationErrors.Add("Somtoday:SchoolUUID must be a non-empty array");
            }

            foreach (string configuredSchoolUuid in configuredSchoolUuids)
            {
                if (!Guid.TryParse(configuredSchoolUuid, out Guid schoolUuid))
                {
                    validationErrors.Add($"Somtoday:SchoolUUID contains an invalid UUID: '{configuredSchoolUuid}'");
                    continue;
                }

                if (!uniqueSchoolUuids.Add(schoolUuid))
                {
                    validationErrors.Add($"Somtoday:SchoolUUID contains a duplicate UUID: '{schoolUuid}'");
                    continue;
                }

                schoolUuids.Add(schoolUuid);
            }

            string clientId = configuration["Somtoday:ClientId"];
            if (string.IsNullOrWhiteSpace(clientId))
            {
                validationErrors.Add("Somtoday:ClientId is missing");
            }

            if (string.IsNullOrWhiteSpace(resolvedClientSecret))
            {
                validationErrors.Add("The resolved Somtoday client secret is missing");
            }

            string configuredEnvironment = configuration["Somtoday:Environment"];
            SomEnvironmentConfig somEnvironment = ParseEnvironment(configuredEnvironment, validationErrors);

            string blobServiceUri = configuration["Storage:AzureBlob:ServiceUri"];
            string blobConnectionString = configuration["Storage:AzureBlob:ConnectionString"];
            string blobContainer = configuration["Storage:AzureBlob:Container"];
            string outputPrefix = configuration["Output:Folder"];

            if (!isDevelopment && !string.IsNullOrWhiteSpace(blobConnectionString))
            {
                validationErrors.Add("Storage:AzureBlob:ConnectionString is allowed only in Development");
            }

            if (string.IsNullOrWhiteSpace(blobServiceUri) && !isDevelopment)
            {
                validationErrors.Add("Storage:AzureBlob:ServiceUri is required outside Development");
            }
            else if (string.IsNullOrWhiteSpace(blobServiceUri) && string.IsNullOrWhiteSpace(blobConnectionString))
            {
                validationErrors.Add("Storage:AzureBlob:ServiceUri or a Development connection string is required");
            }
            else if (!string.IsNullOrWhiteSpace(blobServiceUri)
                && (!Uri.TryCreate(blobServiceUri, UriKind.Absolute, out Uri serviceUri)
                    || (serviceUri.Scheme != Uri.UriSchemeHttps && serviceUri.Scheme != Uri.UriSchemeHttp)))
            {
                validationErrors.Add("Storage:AzureBlob:ServiceUri must be an absolute HTTP(S) URI");
            }

            if (string.IsNullOrWhiteSpace(blobContainer))
            {
                validationErrors.Add("Storage:AzureBlob:Container is missing");
            }

            try
            {
                outputPrefix = BlobPathHelper.NormalizePrefix(outputPrefix);
            }
            catch (ArgumentException ex)
            {
                validationErrors.Add($"Output:Folder is invalid: {ex.Message}");
            }

            value = validationErrors.Count == 0
                ? new SyncConfiguration(
                    schoolUuids.ToArray(),
                    clientId.Trim(),
                    resolvedClientSecret,
                    somEnvironment,
                    configuration.GetSection("Locations:IncludedLocationCodes").Get<string[]>() ?? [],
                    configuration.GetSection("Locations:ExcludedLocationCodes").Get<string[]>() ?? [],
                    blobServiceUri?.Trim(),
                    blobConnectionString,
                    blobContainer.Trim(),
                    outputPrefix,
                    configuration.GetValue<bool>("Output:GenerateEmptyCsv"),
                    configuration.GetValue<bool>("SchoolDataSync:EnableGuardianSync"))
                : null;

            errors = validationErrors.ToArray();
            return validationErrors.Count == 0;
        }

        private static SomEnvironmentConfig ParseEnvironment(string configuredEnvironment, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(configuredEnvironment))
            {
                errors.Add("Somtoday:Environment is missing");
                return SomEnvironmentConfig.Prod;
            }

            return char.ToLowerInvariant(configuredEnvironment.Trim()[0]) switch
            {
                'a' => SomEnvironmentConfig.Acceptatie,
                'n' => SomEnvironmentConfig.Nightly,
                't' => SomEnvironmentConfig.Test,
                'p' => SomEnvironmentConfig.Prod,
                _ => AddInvalidEnvironmentError(configuredEnvironment, errors)
            };
        }

        private static SomEnvironmentConfig AddInvalidEnvironmentError(string configuredEnvironment, List<string> errors)
        {
            errors.Add($"Somtoday:Environment is invalid: '{configuredEnvironment}'");
            return SomEnvironmentConfig.Prod;
        }
    }
}

using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Somtoday2MicrosoftSDS.Helpers;
using Somtoday2MicrosoftSDS.Models;

namespace Somtoday2MicrosoftSDS
{
    internal class Program
    {
        private const int MaxConnectionRetries = 20;
        private static ILogger<Program> _logger;

        private sealed record SchoolSyncContext(
            Guid SchoolUuid,
            string SchoolName,
            string SchoolPathSegment,
            OpenAPIHelper Api,
            List<Vestiging> Locations);

        internal static bool ShouldGenerateEmptyCsv(string[] args, bool configuredGenerateEmptyCsv, DateTime today)
        {
            bool requestedByArgument = args.Any(arg => string.Equals(arg, "--empty-csv", StringComparison.OrdinalIgnoreCase));
            bool isYearEnd = today.Month == 7 && today.Day == 31;
            return configuredGenerateEmptyCsv || requestedByArgument || isYearEnd;
        }

        private static async Task<int> Main(string[] args)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Information);
            builder.Services.AddHttpClient();
            builder.Services.AddSingleton<FileHelper>();
            builder.Services.AddSingleton<SomtodaySecretProvider>();

            using IHost host = builder.Build();
            _logger = host.Services.GetRequiredService<ILogger<Program>>();
            FileHelper fileHelper = host.Services.GetRequiredService<FileHelper>();
            SomtodaySecretProvider secretProvider = host.Services.GetRequiredService<SomtodaySecretProvider>();
            IHostApplicationLifetime applicationLifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            await host.StartAsync();
            CancellationToken cancellationToken = applicationLifetime.ApplicationStopping;

            try
            {
                _logger.LogInformation(
                    "Application starting with version: {Version}",
                    System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString());

                string resolvedClientSecret = await secretProvider.ResolveAsync(
                    builder.Configuration["KeyVault:VaultUri"],
                    builder.Configuration["KeyVault:SomtodayClientSecretName"],
                    Environment.GetEnvironmentVariable(SomtodaySecretProvider.BootstrapEnvironmentVariable),
                    builder.Configuration["Somtoday:ClientSecret"],
                    builder.Environment.IsDevelopment(),
                    cancellationToken);

                SettingsHelper.Initialize(builder.Configuration);
                if (!SyncConfiguration.TryCreate(
                    builder.Configuration,
                    resolvedClientSecret,
                    builder.Environment.IsDevelopment(),
                    out SyncConfiguration configuration,
                    out string[] errors))
                {
                    foreach (string error in errors)
                    {
                        _logger.LogError("Configuration error: {Error}", error);
                    }

                    _logger.LogCritical("Configuration validation failed");
                    return 1;
                }

                ILogger<SettingsHelper> settingsLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<SettingsHelper>();
                if (!new SettingsHelper(settingsLogger).ValidateUsernameFormat())
                {
                    _logger.LogCritical("Username format validation failed");
                    return 1;
                }

                bool generateEmptyCsv = ShouldGenerateEmptyCsv(args, configuration.GenerateEmptyCsv, DateTime.Today);
                BlobContainerClient containerClient = BlobClientFactory.CreateContainerClient(configuration);
                await fileHelper.EnsureContainerExistsAsync(containerClient, cancellationToken);
                _logger.LogInformation(
                    "Azure Blob Storage output enabled for container {Container} using {AuthenticationMode}",
                    configuration.BlobContainer,
                    BlobClientFactory.GetAuthenticationMode(configuration));

                IHttpClientFactory httpClientFactory = host.Services.GetRequiredService<IHttpClientFactory>();
                ILogger<OpenAPIHelper> apiLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<OpenAPIHelper>();
                HashSet<Guid> failedSchools = [];
                List<SchoolSyncContext> schools = [];

                foreach (Guid schoolUuid in configuration.SchoolUuids)
                {
                    try
                    {
                        SchoolSyncContext school = await DiscoverSchoolAsync(
                            schoolUuid,
                            configuration,
                            httpClientFactory,
                            apiLogger,
                            cancellationToken);
                        schools.Add(school);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failedSchools.Add(schoolUuid);
                        _logger.LogError(
                            "Failed to discover Somtoday school {SchoolUuid} ({Error})",
                            schoolUuid,
                            SafeExceptionSummary.Create(ex));
                    }
                }

                RemoveSchoolPathCollisions(schools, failedSchools);

                foreach (SchoolSyncContext school in schools)
                {
                    try
                    {
                        if (generateEmptyCsv)
                        {
                            await SaveEmptySchoolOutputAsync(
                                school,
                                configuration,
                                fileHelper,
                                containerClient,
                                cancellationToken);
                        }
                        else
                        {
                            await SaveSchoolOutputAsync(
                                school,
                                configuration,
                                fileHelper,
                                containerClient,
                                cancellationToken);
                        }

                        _logger.LogInformation(
                            "School sync completed successfully: {SchoolName} ({SchoolUuid})",
                            school.SchoolName,
                            school.SchoolUuid);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failedSchools.Add(school.SchoolUuid);
                        _logger.LogError(
                            "School sync failed: {SchoolName} ({SchoolUuid}) ({Error})",
                            school.SchoolName,
                            school.SchoolUuid,
                            SafeExceptionSummary.Create(ex));
                    }
                }

                if (failedSchools.Count > 0)
                {
                    _logger.LogError(
                        "Sync completed with failures for {FailedCount} of {TotalCount} configured schools: {FailedSchoolUuids}",
                        failedSchools.Count,
                        configuration.SchoolUuids.Length,
                        string.Join(", ", failedSchools));
                    return 1;
                }

                _logger.LogInformation("Sync completed successfully for all {SchoolCount} schools", schools.Count);
                return 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Application cancellation requested; synchronization stopped");
                return 1;
            }
            catch (Exception ex)
            {
                _logger.LogCritical(
                    "Application encountered an unexpected error ({Error})",
                    SafeExceptionSummary.Create(ex));
                return 1;
            }
            finally
            {
                await host.StopAsync(CancellationToken.None);
            }
        }

        private static async Task<SchoolSyncContext> DiscoverSchoolAsync(
            Guid schoolUuid,
            SyncConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<OpenAPIHelper> apiLogger,
            CancellationToken cancellationToken)
        {
            OpenAPIHelper api = await ConnectWithRetryAsync(
                schoolUuid,
                configuration,
                httpClientFactory,
                apiLogger,
                cancellationToken);
            Instelling institution = await api.GetInstellingAsync(cancellationToken);
            string schoolPathSegment = BlobPathHelper.SanitizeSegment(institution.Afkorting, "school abbreviation");
            List<Vestiging> locations = await api.GetSelectedVestigingenAsync(
                configuration.IncludedLocationCodes,
                configuration.ExcludedLocationCodes,
                cancellationToken);

            string[] collidingLocationPaths = locations
                .GroupBy(
                    location => BlobPathHelper.SanitizeSegment(location.Afkorting, "location abbreviation"),
                    StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            if (collidingLocationPaths.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Multiple locations map to the same blob path segment: {string.Join(", ", collidingLocationPaths)}");
            }

            _logger.LogInformation(
                "Discovered school {SchoolName} ({SchoolUuid}) with {LocationCount} selected locations",
                institution.Naam,
                schoolUuid,
                locations.Count);

            return new SchoolSyncContext(schoolUuid, institution.Naam, schoolPathSegment, api, locations);
        }

        private static async Task<OpenAPIHelper> ConnectWithRetryAsync(
            Guid schoolUuid,
            SyncConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<OpenAPIHelper> apiLogger,
            CancellationToken cancellationToken)
        {
            OpenAPIHelper api = null;

            for (int attempt = 1; attempt <= MaxConnectionRetries + 1; attempt++)
            {
                api = new OpenAPIHelper(
                    configuration.ClientId,
                    configuration.ClientSecret,
                    schoolUuid,
                    configuration.SomEnvironment,
                    httpClientFactory,
                    apiLogger);
                await api.ConnectAsync(cancellationToken);

                if (api.IsConnected)
                {
                    return api;
                }

                if (attempt <= MaxConnectionRetries)
                {
                    _logger.LogWarning(
                        "Retrying connection to Somtoday school {SchoolUuid} (attempt {Attempt}/{MaxAttempts})",
                        schoolUuid,
                        attempt,
                        MaxConnectionRetries);
                    await Task.Delay(2000, cancellationToken);
                }
            }

            throw new InvalidOperationException(
                $"Failed to connect to Somtoday school {schoolUuid} after {MaxConnectionRetries} retries");
        }

        private static void RemoveSchoolPathCollisions(List<SchoolSyncContext> schools, HashSet<Guid> failedSchools)
        {
            SchoolSyncContext[] collidingSchools = schools
                .GroupBy(school => school.SchoolPathSegment, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .SelectMany(group => group)
                .ToArray();

            foreach (SchoolSyncContext school in collidingSchools)
            {
                failedSchools.Add(school.SchoolUuid);
                _logger.LogError(
                    "School {SchoolName} ({SchoolUuid}) maps to duplicate blob path segment {SchoolPathSegment}",
                    school.SchoolName,
                    school.SchoolUuid,
                    school.SchoolPathSegment);
            }

            schools.RemoveAll(school => failedSchools.Contains(school.SchoolUuid));
        }

        private static async Task SaveSchoolOutputAsync(
            SchoolSyncContext school,
            SyncConfiguration configuration,
            FileHelper fileHelper,
            BlobContainerClient containerClient,
            CancellationToken cancellationToken)
        {
            List<VestigingModel> allInfo = await school.Api.DownloadAllInfoAsync(
                school.Locations,
                configuration.EnableGuardianSync,
                cancellationToken);

            foreach (VestigingModel info in allInfo)
            {
                string basePrefix = GetLocationPrefix(configuration, school, info.Vestiging);
                string v1Prefix = BlobPathHelper.Combine(basePrefix, "v1");
                string v2Prefix = BlobPathHelper.Combine(basePrefix, "v2");

                _logger.LogInformation(
                    "Processing {SchoolName}/{LocationName}: {GroupsCount} groups, {TeachersCount} teachers, {StudentsCount} students, {ParentsCount} parents",
                    school.SchoolName,
                    info.Vestiging.Naam,
                    info.Lesgroepen.Count,
                    info.Medewerkers.Count,
                    info.Leerlingen.Count,
                    info.OuderVerzorgers.Count);

                await fileHelper.SaveV1ToBlobAsync(
                    new SDScsvHelperV1(info).ConvertToSDSCSV(),
                    containerClient,
                    v1Prefix,
                    cancellationToken);
                await fileHelper.SaveV2ToBlobAsync(
                    new SDScsvHelperV2(info).ConvertToSDSCSV(),
                    containerClient,
                    v2Prefix,
                    cancellationToken);
            }
        }

        private static async Task SaveEmptySchoolOutputAsync(
            SchoolSyncContext school,
            SyncConfiguration configuration,
            FileHelper fileHelper,
            BlobContainerClient containerClient,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Generating empty SDS CSV files with headers only for {SchoolName}",
                school.SchoolName);

            foreach (Vestiging location in school.Locations)
            {
                string basePrefix = GetLocationPrefix(configuration, school, location);
                await fileHelper.SaveEmptyV1ToBlobAsync(
                    containerClient,
                    BlobPathHelper.Combine(basePrefix, "v1"),
                    configuration.EnableGuardianSync,
                    cancellationToken);
                await fileHelper.SaveEmptyV2ToBlobAsync(
                    containerClient,
                    BlobPathHelper.Combine(basePrefix, "v2"),
                    configuration.EnableGuardianSync,
                    cancellationToken);
            }
        }

        private static string GetLocationPrefix(
            SyncConfiguration configuration,
            SchoolSyncContext school,
            Vestiging location)
        {
            string locationPathSegment = BlobPathHelper.SanitizeSegment(location.Afkorting, "location abbreviation");
            return BlobPathHelper.Combine(configuration.OutputPrefix, school.SchoolPathSegment, locationPathSegment);
        }
    }
}

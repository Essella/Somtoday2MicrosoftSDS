using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Somtoday2MicrosoftSDS.Helpers;
using Somtoday2MicrosoftSDS.Models;

namespace Somtoday2MicrosoftSDS
{
    internal class Program
    {
        private const int TotalConnectionAttempts = 4;
        private static readonly TimeSpan ConnectionRetryDelay = TimeSpan.FromSeconds(2);
        private static ILogger<Program> _logger;

        private sealed record SchoolSyncContext(
            Guid SchoolUuid,
            string SchoolName,
            string InstitutionAbbreviation,
            OpenAPIHelper Api,
            List<Vestiging> Locations);

        private sealed record ResolvedLocationContext(
            Guid SchoolUuid,
            ResolvedExportPopulation Population);

        internal static bool ShouldGenerateEmptyCsv(string[] args, bool configuredGenerateEmptyCsv, DateOnly today)
        {
            bool requestedByArgument = args.Any(arg => string.Equals(arg, "--empty-csv", StringComparison.OrdinalIgnoreCase));
            bool isYearEnd = today.Month == 7 && today.Day == 31;
            return configuredGenerateEmptyCsv || requestedByArgument || isYearEnd;
        }

        internal static string CreateRunId()
        {
            return Guid.CreateVersion7().ToString("N");
        }

        internal static HostApplicationBuilder CreateHostApplicationBuilder()
        {
            return Host.CreateApplicationBuilder();
        }

        private static async Task<int> Main(string[] args)
        {
            HostApplicationBuilder builder = CreateHostApplicationBuilder();
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
                DateTimeOffset runStartedUtc = TimeProvider.System.GetUtcNow();
                string runId = CreateRunId();
                DateOnly runDate = AmsterdamTimeHelper.GetDate(runStartedUtc);
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

                bool generateEmptyCsv = ShouldGenerateEmptyCsv(args, configuration.GenerateEmptyCsv, runDate);
                BlobStorageContext storageContext = BlobClientFactory.CreateStorageContext(configuration);
                AzureBlobPublicationStore publicationStore = new(storageContext);
                await publicationStore.EnsureContainerExistsAsync(cancellationToken);
                DatasetPublisher publisher = new(
                    publicationStore,
                    host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<DatasetPublisher>(),
                    configuration.OutputPrefix,
                    runStartedUtc,
                    runId);
                await publisher.CleanupStaleStagingAsync(cancellationToken);
                _logger.LogInformation(
                    "Azure Blob Storage output enabled for container {Container} using {AuthenticationMode}",
                    configuration.BlobContainer,
                    BlobClientFactory.GetAuthenticationMode(configuration));

                IHttpClientFactory httpClientFactory = host.Services.GetRequiredService<IHttpClientFactory>();
                ILogger<OpenAPIHelper> apiLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<OpenAPIHelper>();
                HashSet<Guid> failedSchools = [];
                HashSet<Guid> unavailableSchools = [];
                List<SchoolSyncContext> schools = [];
                IReadOnlyList<Instelling> publicInstitutions = await OpenAPIHelper.GetPublicInstitutionsAsync(
                    httpClientFactory,
                    cancellationToken);

                foreach (Guid schoolUuid in configuration.SchoolUuids)
                {
                    try
                    {
                        Instelling publicInstitution = OpenAPIHelper.SelectInstitution(
                            publicInstitutions,
                            schoolUuid);
                        SchoolSyncContext school = await DiscoverSchoolAsync(
                            schoolUuid,
                            publicInstitution,
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
                        unavailableSchools.Add(schoolUuid);
                        _logger.LogError(
                            "Failed to discover Somtoday school {SchoolUuid} ({Error})",
                            schoolUuid,
                            SafeExceptionSummary.Create(ex));
                    }
                }

                OutputLayoutPlan outputPlan = OutputLayoutPlanner.Create(
                    schools.Select(school => new OutputLayoutSchool(
                        school.SchoolUuid,
                        school.InstitutionAbbreviation,
                        school.Locations)),
                    configuration.OutputPrefix,
                    configuration.SeparateByInstitution,
                    configuration.SeparateByLocation);

                failedSchools.UnionWith(outputPlan.FailedSchoolUuids);
                unavailableSchools.UnionWith(outputPlan.FailedSchoolUuids);
                foreach (OutputLayoutIssue issue in outputPlan.Issues)
                {
                    _logger.LogError(
                        "Output layout failed for Somtoday schools {SchoolUuids}: {Reason}",
                        string.Join(", ", issue.SchoolUuids),
                        issue.Message);
                }

                Dictionary<(Guid SchoolUuid, Guid LocationUuid), ResolvedLocationContext> populations = [];
                if (!generateEmptyCsv)
                {
                    foreach (SchoolSyncContext school in schools.Where(
                        school => !unavailableSchools.Contains(school.SchoolUuid)))
                    {
                        try
                        {
                            IReadOnlyList<ResolvedLocationContext> schoolPopulations =
                                await DownloadSchoolPopulationsAsync(
                                    school,
                                    configuration,
                                    cancellationToken);

                            foreach (ResolvedLocationContext population in schoolPopulations)
                            {
                                populations.Add(
                                    (population.SchoolUuid, population.Population.Vestiging.Uuid),
                                    population);
                            }
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            failedSchools.Add(school.SchoolUuid);
                            unavailableSchools.Add(school.SchoolUuid);
                            _logger.LogError(
                                "School data download failed: {SchoolName} ({SchoolUuid}) ({Error})",
                                school.SchoolName,
                                school.SchoolUuid,
                                SafeExceptionSummary.Create(ex));
                        }
                    }

                }

                foreach (OutputPublicationScope scope in outputPlan.Scopes)
                {
                    if (generateEmptyCsv)
                    {
                        await PublishEmptyScopeAsync(
                            scope,
                            unavailableSchools,
                            configuration,
                            fileHelper,
                            publisher,
                            failedSchools,
                            cancellationToken);
                    }
                    else
                    {
                        await PublishNormalScopeAsync(
                            scope,
                            unavailableSchools,
                            populations,
                            runDate,
                            configuration.EnableGuardianSync,
                            fileHelper,
                            publisher,
                            failedSchools,
                            cancellationToken);
                    }
                }

                foreach (SchoolSyncContext school in schools.Where(
                    school => !failedSchools.Contains(school.SchoolUuid)))
                {
                    _logger.LogInformation(
                        "School sync completed successfully: {SchoolName} ({SchoolUuid})",
                        school.SchoolName,
                        school.SchoolUuid);
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
            Instelling institution,
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
                _logger,
                cancellationToken);
            List<Vestiging> locations = await api.GetSelectedVestigingenAsync(
                configuration.IncludedLocationCodes,
                configuration.ExcludedLocationCodes,
                cancellationToken);

            _logger.LogInformation(
                "Discovered school {SchoolName} ({SchoolUuid}) with {LocationCount} selected locations",
                institution.Naam,
                schoolUuid,
                locations.Count);

            return new SchoolSyncContext(
                schoolUuid,
                institution.Naam,
                institution.Afkorting,
                api,
                locations);
        }

        internal static async Task<OpenAPIHelper> ConnectWithRetryAsync(
            Guid schoolUuid,
            SyncConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<OpenAPIHelper> apiLogger,
            ILogger<Program> programLogger,
            CancellationToken cancellationToken,
            Func<TimeSpan, CancellationToken, Task> delayAsync = null)
        {
            OpenAPIHelper api = null;
            delayAsync ??= Task.Delay;

            for (int attempt = 1; attempt <= TotalConnectionAttempts; attempt++)
            {
                api = new OpenAPIHelper(
                    configuration.ClientId,
                    configuration.ClientSecret,
                    schoolUuid,
                    configuration.SomEnvironment,
                    httpClientFactory,
                    apiLogger);
                SomtodayAuthenticationResult result = await api.ConnectAsync(cancellationToken);

                if (result == SomtodayAuthenticationResult.Succeeded)
                {
                    return api;
                }

                if (result == SomtodayAuthenticationResult.PermanentFailure)
                {
                    break;
                }

                if (attempt == TotalConnectionAttempts)
                {
                    break;
                }

                programLogger.LogWarning(
                    "Retrying connection to Somtoday school {SchoolUuid} (next attempt {NextAttempt}/{TotalAttempts})",
                    schoolUuid,
                    attempt + 1,
                    TotalConnectionAttempts);
                await delayAsync(ConnectionRetryDelay, cancellationToken);
            }

            throw new InvalidOperationException(
                $"Failed to connect to Somtoday school {schoolUuid}");
        }

        private static async Task<IReadOnlyList<ResolvedLocationContext>> DownloadSchoolPopulationsAsync(
            SchoolSyncContext school,
            SyncConfiguration configuration,
            CancellationToken cancellationToken)
        {
            List<VestigingModel> allInfo = await school.Api.DownloadAllInfoAsync(
                school.Locations,
                configuration.EnableGuardianSync,
                cancellationToken);
            List<ResolvedLocationContext> populations = [];

            foreach (VestigingModel info in allInfo)
            {
                _logger.LogInformation(
                    "Processing {SchoolName}/{LocationName}: {GroupsCount} groups, {TeachersCount} teachers, {StudentsCount} students, {ParentsCount} parents",
                    school.SchoolName,
                    info.Vestiging.Naam,
                    info.Lesgroepen.Count,
                    info.Medewerkers.Count,
                    info.Leerlingen.Count,
                    info.OuderVerzorgers.Count);

                ResolvedExportPopulation population = ExportPopulationResolver.Resolve(info);
                LogGuardianNameExclusions(population, _logger);
                if (!ShouldPublishLocation(population, school.SchoolName, _logger))
                {
                    continue;
                }

                populations.Add(new ResolvedLocationContext(school.SchoolUuid, population));
            }

            return populations;
        }

        internal static void LogGuardianNameExclusions(
            ResolvedExportPopulation population,
            ILogger<Program> logger)
        {
            if (population.GuardiansExcludedForMissingName == 0)
            {
                return;
            }

            logger.LogWarning(
                "Excluded {GuardianCount} otherwise eligible guardian records because required name fields are missing",
                population.GuardiansExcludedForMissingName);
        }

        internal static bool ShouldPublishLocation(
            ResolvedExportPopulation population,
            string schoolName,
            ILogger<Program> logger)
        {
            if (population.Classes.Count > 0)
            {
                return true;
            }

            logger.LogWarning(
                "Excluding {SchoolName}/{LocationName} from planned datasets: no class has a non-empty name, at least one resolved teacher, and at least one resolved student",
                schoolName,
                population.Vestiging.Naam);
            return false;
        }

        private static async Task PublishNormalScopeAsync(
            OutputPublicationScope scope,
            IReadOnlySet<Guid> unavailableSchools,
            IReadOnlyDictionary<(Guid SchoolUuid, Guid LocationUuid), ResolvedLocationContext> populations,
            DateOnly runDate,
            bool includeGuardianSync,
            FileHelper fileHelper,
            DatasetPublisher publisher,
            HashSet<Guid> failedSchools,
            CancellationToken cancellationToken)
        {
            OutputPublicationScope availableScope = scope.Excluding(unavailableSchools);
            if (availableScope.SchoolUuids.Count == 0)
            {
                return;
            }

            ResolvedLocationContext[] includedLocations = availableScope.Locations
                .Select(location => populations.TryGetValue(
                    (location.SchoolUuid, location.Location.Uuid),
                    out ResolvedLocationContext population)
                    ? population
                    : null)
                .Where(population => population is not null)
                .ToArray();

            if (includedLocations.Length == 0)
            {
                _logger.LogWarning(
                    "Skipping publication scope {BlobPrefix}: no selected location has an exportable class; existing Blob output is unchanged",
                    availableScope.BasePrefix);
                return;
            }

            Guid[] participantSchoolUuids = includedLocations
                .Select(location => location.SchoolUuid)
                .Distinct()
                .ToArray();
            ResolvedExportPopulation[] scopePopulations = includedLocations
                .Select(location => location.Population)
                .ToArray();

            await PublishVersionAsync(
                "V1",
                BlobPathHelper.Combine(availableScope.BasePrefix, "v1"),
                participantSchoolUuids,
                failedSchools,
                () => publisher.PublishAsync(
                    fileHelper.CreateV1Dataset(
                        new SDScsvHelperV1(scopePopulations, runDate).ConvertToSDSCSV(),
                        includeGuardianSync),
                    BlobPathHelper.Combine(availableScope.BasePrefix, "v1"),
                    cancellationToken),
                _logger,
                cancellationToken);

            await PublishVersionAsync(
                "V2.1",
                BlobPathHelper.Combine(availableScope.BasePrefix, "v2"),
                participantSchoolUuids,
                failedSchools,
                () => publisher.PublishAsync(
                    fileHelper.CreateV2Dataset(
                        new SDScsvHelperV2(scopePopulations, runDate).ConvertToSDSCSV(),
                        includeGuardianSync),
                    BlobPathHelper.Combine(availableScope.BasePrefix, "v2"),
                    cancellationToken),
                _logger,
                cancellationToken);
        }

        private static async Task PublishEmptyScopeAsync(
            OutputPublicationScope scope,
            IReadOnlySet<Guid> unavailableSchools,
            SyncConfiguration configuration,
            FileHelper fileHelper,
            DatasetPublisher publisher,
            HashSet<Guid> failedSchools,
            CancellationToken cancellationToken)
        {
            OutputPublicationScope availableScope = scope.Excluding(unavailableSchools);
            IReadOnlyList<Guid> participantSchoolUuids = availableScope.SchoolUuids;
            if (participantSchoolUuids.Count == 0)
            {
                return;
            }

            _logger.LogInformation(
                "Generating empty SDS CSV files with headers only for scope {BlobPrefix}",
                availableScope.BasePrefix);

            await PublishVersionAsync(
                "V1",
                BlobPathHelper.Combine(availableScope.BasePrefix, "v1"),
                participantSchoolUuids,
                failedSchools,
                () => publisher.PublishAsync(
                    fileHelper.CreateEmptyV1Dataset(configuration.EnableGuardianSync),
                    BlobPathHelper.Combine(availableScope.BasePrefix, "v1"),
                    cancellationToken),
                _logger,
                cancellationToken);

            await PublishVersionAsync(
                "V2.1",
                BlobPathHelper.Combine(availableScope.BasePrefix, "v2"),
                participantSchoolUuids,
                failedSchools,
                () => publisher.PublishAsync(
                    fileHelper.CreateEmptyV2Dataset(configuration.EnableGuardianSync),
                    BlobPathHelper.Combine(availableScope.BasePrefix, "v2"),
                    cancellationToken),
                _logger,
                cancellationToken);
        }

        internal static async Task PublishVersionAsync(
            string version,
            string blobPrefix,
            IReadOnlyList<Guid> participantSchoolUuids,
            HashSet<Guid> failedSchools,
            Func<Task<DatasetPublicationResult>> publish,
            ILogger<Program> logger,
            CancellationToken cancellationToken)
        {
            try
            {
                DatasetPublicationResult result = await publish();
                if (result == DatasetPublicationResult.Failed)
                {
                    failedSchools.UnionWith(participantSchoolUuids);
                    logger.LogError(
                        "Publication failed for {SdsVersion} dataset at {BlobPrefix} for Somtoday schools {SchoolUuids}; the previous complete app dataset was restored",
                        version,
                        blobPrefix,
                        string.Join(", ", participantSchoolUuids));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (PublicationRollbackException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedSchools.UnionWith(participantSchoolUuids);
                logger.LogError(
                    "Failed to publish {SdsVersion} dataset at {BlobPrefix} for Somtoday schools {SchoolUuids} ({Error})",
                    version,
                    blobPrefix,
                    string.Join(", ", participantSchoolUuids),
                    SafeExceptionSummary.Create(ex));
            }
        }
    }
}

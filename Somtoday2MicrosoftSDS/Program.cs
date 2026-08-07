using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Somtoday2MicrosoftSDS.Helpers;
using Somtoday2MicrosoftSDS.Models;

namespace Somtoday2MicrosoftSDS;

internal class Program
{
    internal const string SdsGraphHttpClientName = "SdsGraph";
    internal const string SdsUploadHttpClientName = "SdsUpload";
    internal const string SomtodayAuthenticationHttpClientName = "SomtodayAuthentication";
    internal const string SomtodayApiHttpClientName = "SomtodayApi";
    internal const string SomtodayPublicHttpClientName = "SomtodayPublic";
    private const int TotalConnectionAttempts = 4;
    private static readonly TimeSpan ConnectionRetryDelay = TimeSpan.FromSeconds(2);
    private static ILogger<Program> _logger;

    private sealed record SchoolSyncContext(
        Guid SchoolUuid,
        string SchoolName,
        OpenAPIHelper Api,
        List<Vestiging> Locations);

    internal static bool ShouldUseHeaderOnlyMode(DateOnly today)
    {
        return today is { Month: 7, Day: 31 };
    }

    internal static HostApplicationBuilder CreateHostApplicationBuilder()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services
            .AddHttpClient(SdsGraphHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(CreateNoRedirectHandler);
        builder.Services
            .AddHttpClient(SdsUploadHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(CreateNoRedirectHandler);
        builder.Services
            .AddHttpClient(SomtodayAuthenticationHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(CreateNoRedirectHandler);
        builder.Services
            .AddHttpClient(SomtodayApiHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(CreateNoRedirectHandler);
        builder.Services
            .AddHttpClient(SomtodayPublicHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(CreateNoRedirectHandler);
        return builder;
    }

    internal static SocketsHttpHandler CreateNoRedirectHandler()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false
        };
    }

    private static async Task<int> Main(string[] args)
    {
        HostApplicationBuilder builder = CreateHostApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Services.AddSingleton<FileHelper>();

        using IHost host = builder.Build();
        _logger = host.Services.GetRequiredService<ILogger<Program>>();
        IHostApplicationLifetime applicationLifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        await host.StartAsync();
        CancellationToken cancellationToken = applicationLifetime.ApplicationStopping;

        try
        {
            DateTimeOffset runStartedUtc = TimeProvider.System.GetUtcNow();
            DateOnly runDate = AmsterdamTimeHelper.GetDate(runStartedUtc);
            _logger.LogInformation(
                "Application starting with version: {Version}",
                System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString());

            SettingsHelper.Initialize(builder.Configuration);
            if (!SyncConfiguration.TryCreate(
                builder.Configuration,
                builder.Environment.IsDevelopment(),
                out SyncConfiguration configuration,
                out string[] errors))
            {
                foreach (string error in errors)
                {
                    _logger.LogError("Configuration error: {Error}", error);
                }

                return 1;
            }

            ILogger<SettingsHelper> settingsLogger = host.Services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger<SettingsHelper>();
            if (!new SettingsHelper(settingsLogger).ValidateUsernameFormat())
            {
                _logger.LogCritical("Username format validation failed");
                return 1;
            }

            IHttpClientFactory httpClientFactory = host.Services.GetRequiredService<IHttpClientFactory>();
            SdsGraphClient sdsClient = new(
                httpClientFactory.CreateClient(SdsGraphHttpClientName),
                httpClientFactory.CreateClient(SdsUploadHttpClientName),
                new DefaultAzureCredential());
            SdsConnector connector = await sdsClient.GetConnectorAsync(
                configuration.InboundFlowId,
                cancellationToken);
            _logger.LogInformation(
                "Resolved the SDS connector; this run will create one {SdsFormat} dataset",
                connector.Format == SdsDatasetFormat.V1 ? "V1" : "V2.1");

            bool useHeaderOnlyMode = ShouldUseHeaderOnlyMode(runDate);
            HashSet<Guid> failedSchools = [];
            List<SchoolSyncContext> schools = [];
            IReadOnlyList<Instelling> publicInstitutions = await OpenAPIHelper.GetPublicInstitutionsAsync(
                httpClientFactory,
                cancellationToken);
            ILogger<OpenAPIHelper> apiLogger = host.Services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger<OpenAPIHelper>();

            foreach (Guid schoolUuid in configuration.SchoolUuids)
            {
                try
                {
                    Instelling publicInstitution = OpenAPIHelper.SelectInstitution(publicInstitutions, schoolUuid);
                    schools.Add(await DiscoverSchoolAsync(
                        schoolUuid,
                        publicInstitution,
                        configuration,
                        httpClientFactory,
                        apiLogger,
                        cancellationToken));
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

            if (schools.Count == 0)
            {
                _logger.LogError("No configured Somtoday school could be included; no SDS upload was started");
                return 1;
            }

            PublicationDataset dataset;
            if (useHeaderOnlyMode)
            {
                _logger.LogInformation("Generating one complete SDS dataset with headers only");
                dataset = CreateEmptyDataset(
                    connector.Format,
                    configuration.EnableGuardianSync,
                    host.Services.GetRequiredService<FileHelper>());
            }
            else
            {
                List<ResolvedExportPopulation> populations = [];
                foreach (SchoolSyncContext school in schools)
                {
                    try
                    {
                        populations.AddRange(await DownloadSchoolPopulationsAsync(
                            school,
                            configuration,
                            cancellationToken));
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failedSchools.Add(school.SchoolUuid);
                        _logger.LogError(
                            "School data download failed: {SchoolName} ({SchoolUuid}) ({Error})",
                            school.SchoolName,
                            school.SchoolUuid,
                            SafeExceptionSummary.Create(ex));
                    }
                }

                if (populations.Count == 0)
                {
                    _logger.LogWarning(
                        "No selected location from a successful school contains an exportable class; no SDS upload was started");
                    return failedSchools.Count == 0 ? 0 : 1;
                }

                dataset = CreateDataset(
                    connector.Format,
                    populations,
                    runDate,
                    configuration.EnableGuardianSync,
                    host.Services.GetRequiredService<FileHelper>());
            }

            await sdsClient.UploadAndValidateAsync(connector.Id, dataset, cancellationToken);
            _logger.LogInformation(
                "SDS upload and validation completed successfully for {FileCount} files",
                dataset.Files.Count);

            if (failedSchools.Count > 0)
            {
                _logger.LogError(
                    "The successful school subset was uploaded, but {FailedCount} of {TotalCount} configured schools failed: {FailedSchoolUuids}",
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

    internal static PublicationDataset CreateDataset(
        SdsDatasetFormat format,
        IReadOnlyList<ResolvedExportPopulation> populations,
        DateOnly runDate,
        bool includeGuardianSync,
        FileHelper fileHelper)
    {
        return format == SdsDatasetFormat.V1
            ? fileHelper.CreateV1Dataset(
                new SDScsvHelperV1(populations, runDate).ConvertToSDSCSV(),
                includeGuardianSync)
            : fileHelper.CreateV2Dataset(
                new SDScsvHelperV2(populations, runDate).ConvertToSDSCSV(),
                includeGuardianSync);
    }

    internal static PublicationDataset CreateEmptyDataset(
        SdsDatasetFormat format,
        bool includeGuardianSync,
        FileHelper fileHelper)
    {
        return format == SdsDatasetFormat.V1
            ? fileHelper.CreateEmptyV1Dataset(includeGuardianSync)
            : fileHelper.CreateEmptyV2Dataset(includeGuardianSync);
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
        if (locations.Any(location => string.IsNullOrWhiteSpace(location.Afkorting)))
        {
            throw new InvalidOperationException("A selected Somtoday location has no abbreviation");
        }

        _logger.LogInformation(
            "Discovered school {SchoolName} ({SchoolUuid}) with {LocationCount} selected locations",
            institution.Naam,
            schoolUuid,
            locations.Count);
        return new SchoolSyncContext(schoolUuid, institution.Naam, api, locations);
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
        delayAsync ??= Task.Delay;
        for (int attempt = 1; attempt <= TotalConnectionAttempts; attempt++)
        {
            OpenAPIHelper api = new(
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

            if (result == SomtodayAuthenticationResult.PermanentFailure || attempt == TotalConnectionAttempts)
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

        throw new InvalidOperationException($"Failed to connect to Somtoday school {schoolUuid}");
    }

    private static async Task<IReadOnlyList<ResolvedExportPopulation>> DownloadSchoolPopulationsAsync(
        SchoolSyncContext school,
        SyncConfiguration configuration,
        CancellationToken cancellationToken)
    {
        List<VestigingModel> allInfo = await school.Api.DownloadAllInfoAsync(
            school.Locations,
            configuration.EnableGuardianSync,
            cancellationToken);
        List<ResolvedExportPopulation> populations = [];
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
            if (ShouldPublishLocation(population, school.SchoolName, _logger))
            {
                populations.Add(population);
            }
        }

        return populations;
    }

    internal static void LogGuardianNameExclusions(
        ResolvedExportPopulation population,
        ILogger<Program> logger)
    {
        if (population.GuardiansExcludedForMissingName > 0)
        {
            logger.LogWarning(
                "Excluded {GuardianCount} otherwise eligible guardian records because required name fields are missing",
                population.GuardiansExcludedForMissingName);
        }
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
}

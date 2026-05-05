using SyncIdPreview.Helpers;
using SyncIdPreview.Models;
using System.Runtime.InteropServices;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SyncIdPreview
{
    internal class Program
    {
        // Configuration stored in fields
        private static bool _booleanFilterBylocation;
        private static bool _seperateOutputFolderForEachLocation;
        private static string[] _includedLocationCode;
        private static string _schoolUUID;
        private static string _clientId;
        private static string _clientSecret;
        private static string _outputFolder;
        private static bool _enableGuardianSync;
        private static SomEnvironmentConfig _somOmgeving;
        private static int _sdsCsvVersion;
        private static bool _clearCsvAtYearEnd;
        private static bool _generateEmptyCsv;
        private static StorageMode _storageMode;
        private static string _azureStorageConnectionString;
        private static string _azureStorageContainer;

        private static ILogger<Program> _logger;
        private static OpenAPIHelper _oh;
        private static FileHelper _fh;

        #region Console control handler
        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(EventHandler handler, bool add);

        private delegate bool EventHandler(CtrlType sig);
        private static EventHandler _handler;

        enum CtrlType
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }

        private static bool Handler(CtrlType sig)
        {
            switch (sig)
            {
                case CtrlType.CTRL_C_EVENT:
                case CtrlType.CTRL_LOGOFF_EVENT:
                case CtrlType.CTRL_SHUTDOWN_EVENT:
                case CtrlType.CTRL_CLOSE_EVENT:
                default:
                    _logger?.LogWarning("Application interrupted by user");
                    return false;
            }
        }
        #endregion

        static bool InitializeConfiguration(IConfiguration configuration)
        {
            bool isValid = true;

            if (!TryGetRequiredValue(configuration, "Locations:FilterByLocation", out _booleanFilterBylocation))
            {
                _logger?.LogError("Configuration error: Locations:FilterByLocation is invalid or missing");
                isValid = false;
            }

            if (!TryGetRequiredValue(configuration, "Locations:SeparateOutputFolderForEachLocation", out _seperateOutputFolderForEachLocation))
            {
                _logger?.LogError("Configuration error: Locations:SeparateOutputFolderForEachLocation is invalid or missing");
                isValid = false;
            }

            _includedLocationCode = configuration.GetSection("Locations:IncludedLocationCodes").Get<string[]>() ?? [];
            if (_booleanFilterBylocation && !_includedLocationCode.Any(locationCode => !string.IsNullOrWhiteSpace(locationCode)))
            {
                _logger?.LogError("Configuration error: Locations:IncludedLocationCodes is invalid or missing while Locations:FilterByLocation is true");
                isValid = false;
            }

            _schoolUUID = configuration["Somtoday:SchoolUUID"];
            if (string.IsNullOrWhiteSpace(_schoolUUID))
            {
                _logger?.LogError("Configuration error: Somtoday:SchoolUUID is invalid or missing");
                isValid = false;
            }

            _clientId = configuration["Somtoday:ClientId"];
            if (string.IsNullOrWhiteSpace(_clientId))
            {
                _logger?.LogError("Configuration error: Somtoday:ClientId is invalid or missing");
                isValid = false;
            }

            _clientSecret = configuration["Somtoday:ClientSecret"];
            if (string.IsNullOrWhiteSpace(_clientSecret))
            {
                _logger?.LogError("Configuration error: Somtoday:ClientSecret is invalid or missing");
                isValid = false;
            }

            if (!Enum.TryParse(configuration["Storage:Mode"], ignoreCase: true, out _storageMode))
            {
                _logger?.LogError("Configuration error: Storage:Mode is invalid or missing. Use Disk or AzureBlob");
                isValid = false;
            }

            _azureStorageConnectionString = configuration["Storage:AzureBlob:ConnectionString"];
            _azureStorageContainer = configuration["Storage:AzureBlob:Container"];

            if (_storageMode == StorageMode.AzureBlob)
            {
                if (string.IsNullOrWhiteSpace(_azureStorageConnectionString))
                {
                    _logger?.LogError("Configuration error: Storage:AzureBlob:ConnectionString is invalid or missing for AzureBlob storage mode");
                    isValid = false;
                }

                if (string.IsNullOrWhiteSpace(_azureStorageContainer))
                {
                    _logger?.LogError("Configuration error: Storage:AzureBlob:Container is invalid or missing for AzureBlob storage mode");
                    isValid = false;
                }
            }

            _outputFolder = configuration["Output:Folder"];
            if (_storageMode == StorageMode.Disk && string.IsNullOrWhiteSpace(_outputFolder))
            {
                _logger?.LogError("Configuration error: Output:Folder is invalid or missing");
                isValid = false;
            }
            else if (!string.IsNullOrWhiteSpace(_outputFolder))
            {
                _outputFolder = Path.EndsInDirectorySeparator(_outputFolder) ? _outputFolder : _outputFolder + Path.DirectorySeparatorChar;
            }

            if (!TryGetRequiredValue(configuration, "SchoolDataSync:EnableGuardianSync", out _enableGuardianSync))
            {
                _logger?.LogError("Configuration error: SchoolDataSync:EnableGuardianSync is invalid or missing");
                isValid = false;
            }

            if (string.IsNullOrWhiteSpace(configuration["Somtoday:Environment"]))
            {
                _logger?.LogError("Configuration error: Somtoday:Environment is invalid or missing");
                isValid = false;
            }
            else
            {
                string somOmgevingstring = configuration["Somtoday:Environment"];
                switch (char.ToLower(somOmgevingstring[0]))
                {
                    case 'a':
                        _somOmgeving = SomEnvironmentConfig.Acceptatie;
                        break;
                    case 'n':
                        _somOmgeving = SomEnvironmentConfig.Nightly;
                        break;
                    case 't':
                        _somOmgeving = SomEnvironmentConfig.Test;
                        break;
                    case 'p':
                    default:
                        _somOmgeving = SomEnvironmentConfig.Prod;
                        break;
                }
            }

            if (!TryGetRequiredValue(configuration, "SchoolDataSync:CsvVersion", out _sdsCsvVersion))
            {
                _logger?.LogError("Configuration error: SchoolDataSync:CsvVersion is invalid or missing");
                isValid = false;
            }

            if (!configuration.GetValue<bool?>("Output:ClearCsvAtYearEnd").HasValue)
            {
                _logger?.LogDebug("Configuration: Output:ClearCsvAtYearEnd not specified, using default false");
                _clearCsvAtYearEnd = false;
            }
            else
            {
                _clearCsvAtYearEnd = configuration.GetValue<bool>("Output:ClearCsvAtYearEnd");
            }

            if (!configuration.GetValue<bool?>("Output:GenerateEmptyCsv").HasValue)
            {
                _logger?.LogDebug("Configuration: Output:GenerateEmptyCsv not specified, using default false");
                _generateEmptyCsv = false;
            }
            else
            {
                _generateEmptyCsv = configuration.GetValue<bool>("Output:GenerateEmptyCsv");
            }

            return isValid;
        }

        private static bool TryGetRequiredValue<T>(IConfiguration configuration, string key, out T value)
            where T : struct
        {
            T? configuredValue = configuration.GetValue<T?>(key);
            value = configuredValue.GetValueOrDefault();
            return configuredValue.HasValue;
        }

        private static bool ShouldGenerateEmptyCsv(string[] args)
        {
            bool requestedByArgument = args.Any(arg => string.Equals(arg, "--empty-csv", StringComparison.OrdinalIgnoreCase));
            bool isYearEnd = _clearCsvAtYearEnd && DateTime.Today.Month == 7 && DateTime.Today.Day == 31;

            return _generateEmptyCsv || requestedByArgument || isYearEnd;
        }

        private static async Task GenerateEmptyCsvOutputAsync()
        {
            _logger.LogInformation("Generating empty SDS CSV files with headers only");

            if (_storageMode == StorageMode.AzureBlob)
            {
                BlobContainerClient blobContainerClient = new BlobContainerClient(_azureStorageConnectionString, _azureStorageContainer);
                await _fh.EnsureBlobStructureAsync(blobContainerClient);

                if (_seperateOutputFolderForEachLocation)
                {
                    string[] locationCodes = GetConfiguredLocationCodes().ToArray();
                    if (locationCodes.Length == 0)
                    {
                        _logger.LogWarning("SeperateOutputFolderForEachLocation is enabled, but no location codes are configured because BooleanFilterBylocation is false. Writing empty CSV files to the shared blob output folders.");
                        await _fh.SaveEmptyV1ToBlobAsync(blobContainerClient, "sds/output/v1", _enableGuardianSync);
                        await _fh.SaveEmptyV2ToBlobAsync(blobContainerClient, "sds/output/v2", _enableGuardianSync);
                        return;
                    }

                    foreach (string locationCode in locationCodes)
                    {
                        await _fh.SaveEmptyV1ToBlobAsync(blobContainerClient, $"sds/output/v1/{locationCode}", _enableGuardianSync);
                        await _fh.SaveEmptyV2ToBlobAsync(blobContainerClient, $"sds/output/v2/{locationCode}", _enableGuardianSync);
                    }
                }
                else
                {
                    await _fh.SaveEmptyV1ToBlobAsync(blobContainerClient, "sds/output/v1", _enableGuardianSync);
                    await _fh.SaveEmptyV2ToBlobAsync(blobContainerClient, "sds/output/v2", _enableGuardianSync);
                }

                return;
            }

            foreach (string outputFolder in GetEmptyCsvOutputFolders())
            {
                if (_sdsCsvVersion == 1)
                {
                    _fh.SaveEmptyV1ToDisk(outputFolder, _enableGuardianSync);
                }
                else if (_sdsCsvVersion == 2)
                {
                    _fh.SaveEmptyV2ToDisk(outputFolder, _enableGuardianSync);
                }
            }
        }

        private static IEnumerable<string> GetEmptyCsvOutputFolders()
        {
            if (!_seperateOutputFolderForEachLocation)
            {
                return [_outputFolder];
            }

            if (Directory.Exists(_outputFolder))
            {
                string[] existingLocationFolders = Directory.GetDirectories(_outputFolder);
                if (existingLocationFolders.Length > 0)
                {
                    return existingLocationFolders;
                }
            }

            string[] locationCodes = GetConfiguredLocationCodes().ToArray();
            if (locationCodes.Length > 0)
            {
                return locationCodes.Select(locationCode => Path.Combine(_outputFolder, locationCode));
            }

            _logger.LogWarning("SeperateOutputFolderForEachLocation is enabled, but no existing location folders were found and BooleanFilterBylocation is false. Writing empty CSV files to the shared output folder.");
            return [_outputFolder];
        }

        private static IEnumerable<string> GetConfiguredLocationCodes()
        {
            if (!_booleanFilterBylocation)
            {
                return [];
            }

            return _includedLocationCode
                .Where(locationCode => !string.IsNullOrWhiteSpace(locationCode))
                .Select(locationCode => locationCode.Trim());
        }

        static async Task Main(string[] args)
        {
            // Create Host with Dependency Injection
            var builder = Host.CreateApplicationBuilder(args);

            // Configure logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Information);

            // Register services
            builder.Services.AddHttpClient();
            builder.Services.AddSingleton<FileHelper>();
            builder.Services.AddSingleton<EventLogHelper>();

            using IHost host = builder.Build();

            // Get logger from DI container
            _logger = host.Services.GetRequiredService<ILogger<Program>>();
            _fh = host.Services.GetRequiredService<FileHelper>();

            try
            {
                _logger.LogInformation("Application starting with version: {Version}", 
                    System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString());

                // Initialize configuration
                SettingsHelper.Initialize(builder.Configuration);

                if (!InitializeConfiguration(builder.Configuration))
                {
                    _logger.LogCritical("Configuration validation failed");
                    Environment.Exit(1);
                }

                if (ShouldGenerateEmptyCsv(args))
                {
                    await GenerateEmptyCsvOutputAsync();
                    _logger.LogInformation("Empty SDS CSV generation completed successfully");
                    return;
                }

                // Setup console control handler
                _handler += new EventHandler(Handler);
                SetConsoleCtrlHandler(_handler, true);

                // Validate settings - create SettingsHelper logger
                var settingsHelperLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<SettingsHelper>();
                SettingsHelper settingsHelper = new SettingsHelper(settingsHelperLogger);
                if (!settingsHelper.ValidateUsernameFormat())
                {
                    _logger.LogCritical("Username format validation failed");
                    Environment.Exit(1);
                }

                // Start sync process
                _logger.LogInformation("Sync starting");

                ILogger<OpenAPIHelper> openApiLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<OpenAPIHelper>();
                IHttpClientFactory httpClientFactory = host.Services.GetRequiredService<IHttpClientFactory>();
                _oh = new OpenAPIHelper(_clientId, _clientSecret, _schoolUUID, _somOmgeving, httpClientFactory, openApiLogger);
                await _oh.ConnectAsync();
                int retryCount = 0;
                const int MaxRetries = 20;

                while (!_oh.IsConnected && retryCount < MaxRetries)
                {
                    retryCount++;
                    _logger.LogInformation("Retrying connection to Somtoday (attempt {Attempt}/{MaxAttempts})", retryCount, MaxRetries);
                    await Task.Delay(2000);
                    _oh = new OpenAPIHelper(_clientId, _clientSecret, _schoolUUID, _somOmgeving, httpClientFactory, openApiLogger);
                    await _oh.ConnectAsync();
                }

                if (!_oh.IsConnected)
                {
                    _logger.LogError("Failed to connect to Somtoday after {MaxRetries} attempts", MaxRetries);
                    return;
                }

                // Download and process data
                List<VestigingModel> allInfo = await _oh.DownloadAllInfoAsync(_booleanFilterBylocation, _includedLocationCode, _enableGuardianSync);

                List<SDScsvV1> sdsCsvV1List = new List<SDScsvV1>();
                List<SDScsvV2> sdsCsvV2List = new List<SDScsvV2>();
                BlobContainerClient blobContainerClient = null;

                if (_storageMode == StorageMode.AzureBlob)
                {
                    blobContainerClient = new BlobContainerClient(_azureStorageConnectionString, _azureStorageContainer);
                    await _fh.EnsureBlobStructureAsync(blobContainerClient);
                    _logger.LogInformation("Azure Blob Storage output enabled for container: {Container}", _azureStorageContainer);
                }

                foreach (VestigingModel info in allInfo)
                {
                    _logger.LogInformation("Processing: {VestigingNaam}, {LesgroepenCount} groups, {MedewerkerCount} teachers, {LeerlingenCount} students, {OuderCount} parents",
                        info.Vestiging.Naam, info.Lesgroepen.Count, info.Medewerkers.Count, info.Leerlingen.Count, info.OuderVerzorgers.Count);

                    if (_storageMode == StorageMode.AzureBlob)
                    {
                        SDScsvV1 sdsCsvV1 = new SDScsvHelperV1(info).ConvertToSDSCSV();
                        SDScsvV2 sdsCsvV2 = new SDScsvHelperV2(info).ConvertToSDSCSV();

                        if (_seperateOutputFolderForEachLocation)
                        {
                            string v1Prefix = $"sds/output/v1/{info.Vestiging.Afkorting}";
                            string v2Prefix = $"sds/output/v2/{info.Vestiging.Afkorting}";
                            _logger.LogInformation("Uploading V1 data to blob prefix: {BlobPrefix}", v1Prefix);
                            await _fh.SaveV1ToBlobAsync(sdsCsvV1, blobContainerClient, v1Prefix);
                            _logger.LogInformation("Uploading V2 data to blob prefix: {BlobPrefix}", v2Prefix);
                            await _fh.SaveV2ToBlobAsync(sdsCsvV2, blobContainerClient, v2Prefix);
                        }
                        else
                        {
                            sdsCsvV1List.Add(sdsCsvV1);
                            sdsCsvV2List.Add(sdsCsvV2);
                        }

                        continue;
                    }

                    if (_sdsCsvVersion == 1)
                    {
                        SDScsvHelperV1 sh = new SDScsvHelperV1(info);
                        SDScsvV1 sdsCsv = sh.ConvertToSDSCSV();
                        if (_seperateOutputFolderForEachLocation)
                        {
                            string actualOutputFolder = Path.Combine(_outputFolder, info.Vestiging.Afkorting);
                            _logger.LogInformation("Writing to: {OutputFolder}", actualOutputFolder);
                            _fh.SaveV1ToDisk(sdsCsv, actualOutputFolder);
                        }
                        else
                        {
                            sdsCsvV1List.Add(sdsCsv);
                        }
                    }

                    if (_sdsCsvVersion == 2)
                    {
                        SDScsvHelperV2 sh = new SDScsvHelperV2(info);
                        SDScsvV2 sdsCsv = sh.ConvertToSDSCSV();
                        if (_seperateOutputFolderForEachLocation)
                        {
                            string actualOutputFolder = Path.Combine(_outputFolder, info.Vestiging.Afkorting);
                            _logger.LogInformation("Writing to: {OutputFolder}", actualOutputFolder);
                            _fh.SaveV2ToDisk(sdsCsv, actualOutputFolder);
                        }
                        else
                        {
                            sdsCsvV2List.Add(sdsCsv);
                        }
                    }
                }

                // Write aggregated results
                if (_storageMode == StorageMode.AzureBlob && !_seperateOutputFolderForEachLocation)
                {
                    _logger.LogInformation("Uploading all v1 data to blob prefix: sds/output/v1");
                    await _fh.SaveV1ToBlobAsync(sdsCsvV1List, blobContainerClient, "sds/output/v1");

                    _logger.LogInformation("Uploading all v2 data to blob prefix: sds/output/v2");
                    await _fh.SaveV2ToBlobAsync(sdsCsvV2List, blobContainerClient, "sds/output/v2");
                }

                if (_storageMode == StorageMode.Disk && _sdsCsvVersion == 1 && sdsCsvV1List.Count > 0 && !_seperateOutputFolderForEachLocation)
                {
                    _logger.LogInformation("Writing all v1 data to: {OutputFolder}", _outputFolder);
                    _fh.SaveV1ToDisk(sdsCsvV1List, _outputFolder);
                }

                if (_storageMode == StorageMode.Disk && _sdsCsvVersion == 2 && sdsCsvV2List.Count > 0 && !_seperateOutputFolderForEachLocation)
                {
                    _logger.LogInformation("Writing all v2 data to: {OutputFolder}", _outputFolder);
                    _fh.SaveV2ToDisk(sdsCsvV2List, _outputFolder);
                }

                _logger.LogInformation("Sync completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Application encountered an unexpected error");
                Environment.Exit(1);
            }
            finally
            {
                await Task.Delay(10000);
            }
        }
    }
}

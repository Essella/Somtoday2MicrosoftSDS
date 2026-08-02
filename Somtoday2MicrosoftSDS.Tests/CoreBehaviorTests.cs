using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.Hosting;
using Somtoday2MicrosoftSDS.Helpers;
using Xunit;

namespace Somtoday2MicrosoftSDS.Tests;

public class CoreBehaviorTests
{
    private static readonly Guid FirstSchoolUuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondSchoolUuid = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void TrackedDefaultsContainNoCredentialsAndFailClosed()
    {
        string settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using FileStream settings = File.OpenRead(settingsPath);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonStream(settings)
            .Build();

        Assert.Empty(configuration.GetSection("Somtoday:SchoolUUID").GetChildren());
        Assert.True(string.IsNullOrEmpty(configuration["Somtoday:ClientId"]));
        Assert.True(string.IsNullOrEmpty(configuration["Somtoday:ClientSecret"]));
        Assert.True(string.IsNullOrEmpty(configuration["KeyVault:VaultUri"]));
        Assert.True(string.IsNullOrEmpty(configuration["Storage:AzureBlob:ServiceUri"]));
        Assert.True(string.IsNullOrEmpty(configuration["Storage:AzureBlob:ConnectionString"]));
        Assert.True(configuration.GetValue<bool>("Output:SeparateByInstitution"));
        Assert.False(configuration.GetValue<bool>("Output:SeparateByLocation"));
        Assert.False(SyncConfiguration.TryCreate(
            configuration,
            resolvedClientSecret: string.Empty,
            isDevelopment: false,
            out _,
            out _));
    }

    [Fact]
    public void ConfigurationAcceptsUuidArrayAndIdentityTakesPrecedence()
    {
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string>
        {
            ["Somtoday:SchoolUUID:0"] = FirstSchoolUuid.ToString(),
            ["Storage:AzureBlob:ServiceUri"] = "https://account.blob.core.windows.net",
            ["Storage:AzureBlob:ConnectionString"] = "UseDevelopmentStorage=true"
        });

        bool isValid = SyncConfiguration.TryCreate(
            configuration,
            "client-secret",
            isDevelopment: true,
            out SyncConfiguration result,
            out string[] errors);

        Assert.True(isValid, string.Join(Environment.NewLine, errors));
        Assert.Equal(new[] { FirstSchoolUuid }, result.SchoolUuids);
        Assert.Equal(BlobAuthenticationMode.DefaultAzureCredential, BlobClientFactory.GetAuthenticationMode(result));
    }

    [Fact]
    public void ConfigurationUsesConnectionStringWhenServiceUriIsEmpty()
    {
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string>
        {
            ["Somtoday:SchoolUUID:0"] = FirstSchoolUuid.ToString(),
            ["Storage:AzureBlob:ServiceUri"] = "",
            ["Storage:AzureBlob:ConnectionString"] = "UseDevelopmentStorage=true"
        });

        Assert.True(
            SyncConfiguration.TryCreate(
                configuration,
                "client-secret",
                isDevelopment: true,
                out SyncConfiguration result,
                out string[] errors),
            string.Join(Environment.NewLine, errors));
        Assert.Equal(BlobAuthenticationMode.ConnectionString, BlobClientFactory.GetAuthenticationMode(result));
    }

    [Fact]
    public void OutputGroupingUsesConfirmedDefaultsAndAcceptsOverrides()
    {
        IConfiguration defaults = CreateConfiguration(new Dictionary<string, string>
        {
            ["Somtoday:SchoolUUID:0"] = FirstSchoolUuid.ToString()
        });
        IConfiguration overrides = CreateConfiguration(new Dictionary<string, string>
        {
            ["Somtoday:SchoolUUID:0"] = FirstSchoolUuid.ToString(),
            ["Output:SeparateByInstitution"] = "false",
            ["Output:SeparateByLocation"] = "true"
        });

        Assert.True(SyncConfiguration.TryCreate(
            defaults,
            "client-secret",
            true,
            out SyncConfiguration defaultResult,
            out string[] defaultErrors),
            string.Join(Environment.NewLine, defaultErrors));
        Assert.True(defaultResult.SeparateByInstitution);
        Assert.False(defaultResult.SeparateByLocation);

        Assert.True(SyncConfiguration.TryCreate(
            overrides,
            "client-secret",
            true,
            out SyncConfiguration overrideResult,
            out string[] overrideErrors),
            string.Join(Environment.NewLine, overrideErrors));
        Assert.False(overrideResult.SeparateByInstitution);
        Assert.True(overrideResult.SeparateByLocation);
    }

    [Fact]
    public void ProductionRejectsConnectionStringAndRequiresServiceUri()
    {
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string>
        {
            ["Somtoday:SchoolUUID:0"] = FirstSchoolUuid.ToString(),
            ["Storage:AzureBlob:ServiceUri"] = "",
            ["Storage:AzureBlob:ConnectionString"] = "UseDevelopmentStorage=true"
        });

        Assert.False(SyncConfiguration.TryCreate(
            configuration,
            "client-secret",
            isDevelopment: false,
            out _,
            out string[] errors));
        Assert.Contains(errors, error => error.Contains("allowed only in Development", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("ServiceUri is required", StringComparison.Ordinal));
    }

    [Fact]
    public void ProductionUsesManagedIdentityWhenServiceUriIsConfigured()
    {
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string>
        {
            ["Somtoday:SchoolUUID:0"] = FirstSchoolUuid.ToString(),
            ["Storage:AzureBlob:ServiceUri"] = "https://account.blob.core.windows.net",
            ["Storage:AzureBlob:ConnectionString"] = ""
        });

        Assert.True(
            SyncConfiguration.TryCreate(
                configuration,
                "client-secret",
                isDevelopment: false,
                out SyncConfiguration result,
                out string[] errors),
            string.Join(Environment.NewLine, errors));
        Assert.Equal(BlobAuthenticationMode.DefaultAzureCredential, BlobClientFactory.GetAuthenticationMode(result));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfigurationRejectsHttpBlobServiceUri(bool isDevelopment)
    {
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string>
        {
            ["Somtoday:SchoolUUID:0"] = FirstSchoolUuid.ToString(),
            ["Storage:AzureBlob:ServiceUri"] = "http://account.blob.core.windows.net",
            ["Storage:AzureBlob:ConnectionString"] = ""
        });

        Assert.False(SyncConfiguration.TryCreate(
            configuration,
            "client-secret",
            isDevelopment,
            out _,
            out string[] errors));
        Assert.Contains(
            errors,
            error => error.Contains("absolute HTTPS URI", StringComparison.Ordinal));
    }

    [Fact]
    public void NightlyIsAcceptedOnlyInDevelopment()
    {
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string>
        {
            ["Somtoday:Environment"] = "NIGHTLY",
            ["Somtoday:SchoolUUID:0"] = FirstSchoolUuid.ToString(),
            ["Storage:AzureBlob:ServiceUri"] = "https://account.blob.core.windows.net",
            ["Storage:AzureBlob:ConnectionString"] = ""
        });

        Assert.False(SyncConfiguration.TryCreate(
            configuration,
            "client-secret",
            isDevelopment: false,
            out _,
            out string[] productionErrors));
        Assert.Contains(
            productionErrors,
            error => error.Contains("NIGHTLY is allowed only in Development", StringComparison.Ordinal));

        Assert.True(
            SyncConfiguration.TryCreate(
                configuration,
                "client-secret",
                isDevelopment: true,
                out SyncConfiguration developmentResult,
                out string[] developmentErrors),
            string.Join(Environment.NewLine, developmentErrors));
        Assert.Same(SomEnvironmentConfig.Nightly, developmentResult.SomEnvironment);
    }

    [Theory]
    [InlineData("PROD")]
    [InlineData("TEST")]
    [InlineData("ACCEPTATIE")]
    public void ProductionAcceptsHttpsSomtodayEnvironments(string environment)
    {
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string>
        {
            ["Somtoday:Environment"] = environment,
            ["Somtoday:SchoolUUID:0"] = FirstSchoolUuid.ToString(),
            ["Storage:AzureBlob:ServiceUri"] = "https://account.blob.core.windows.net",
            ["Storage:AzureBlob:ConnectionString"] = ""
        });

        Assert.True(
            SyncConfiguration.TryCreate(
                configuration,
                "client-secret",
                isDevelopment: false,
                out _,
                out string[] errors),
            string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void ConfigurationRejectsScalarInvalidAndDuplicateUuids()
    {
        IConfiguration scalarConfiguration = CreateConfiguration(new Dictionary<string, string>
        {
            ["Somtoday:SchoolUUID"] = FirstSchoolUuid.ToString()
        });
        IConfiguration duplicateConfiguration = CreateConfiguration(new Dictionary<string, string>
        {
            ["Somtoday:SchoolUUID:0"] = FirstSchoolUuid.ToString(),
            ["Somtoday:SchoolUUID:1"] = FirstSchoolUuid.ToString()
        });
        IConfiguration invalidConfiguration = CreateConfiguration(new Dictionary<string, string>
        {
            ["Somtoday:SchoolUUID:0"] = "not-a-uuid"
        });

        Assert.False(SyncConfiguration.TryCreate(scalarConfiguration, "client-secret", true, out _, out _));
        Assert.False(SyncConfiguration.TryCreate(duplicateConfiguration, "client-secret", true, out _, out _));
        Assert.False(SyncConfiguration.TryCreate(invalidConfiguration, "client-secret", true, out _, out _));
    }

    [Fact]
    public void RemovedConfigurationKeysAreNotRequired()
    {
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string>
        {
            ["Somtoday:SchoolUUID:0"] = FirstSchoolUuid.ToString()
        });

        Assert.True(
            SyncConfiguration.TryCreate(configuration, "client-secret", true, out _, out string[] errors),
            string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void EnvironmentVariablesOverrideJsonStyleConfiguration()
    {
        const string prefix = "S2MSDS_TEST_";
        const string clientVariableName = prefix + "Somtoday__ClientId";
        const string institutionVariableName = prefix + "Output__SeparateByInstitution";
        const string locationVariableName = prefix + "Output__SeparateByLocation";
        Environment.SetEnvironmentVariable(clientVariableName, "client-from-environment");
        Environment.SetEnvironmentVariable(institutionVariableName, "false");
        Environment.SetEnvironmentVariable(locationVariableName, "true");

        try
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Somtoday:Environment"] = "PROD",
                    ["Somtoday:ClientId"] = "client-from-json",
                    ["Somtoday:SchoolUUID:0"] = FirstSchoolUuid.ToString(),
                    ["Storage:AzureBlob:ConnectionString"] = "UseDevelopmentStorage=true",
                    ["Storage:AzureBlob:Container"] = "sds",
                    ["Output:Folder"] = "sds/output",
                    ["Output:SeparateByInstitution"] = "true",
                    ["Output:SeparateByLocation"] = "false"
                })
                .AddEnvironmentVariables(prefix)
                .Build();

            Assert.Equal("client-from-environment", configuration["Somtoday:ClientId"]);
            Assert.True(SyncConfiguration.TryCreate(
                configuration,
                "client-secret",
                isDevelopment: true,
                out SyncConfiguration result,
                out string[] errors),
                string.Join(Environment.NewLine, errors));
            Assert.False(result.SeparateByInstitution);
            Assert.True(result.SeparateByLocation);
        }
        finally
        {
            Environment.SetEnvironmentVariable(clientVariableName, null);
            Environment.SetEnvironmentVariable(institutionVariableName, null);
            Environment.SetEnvironmentVariable(locationVariableName, null);
        }
    }

    [Fact]
    public void ApplicationAssemblyDeclaresUserSecretsIdForDevelopmentConfiguration()
    {
        UserSecretsIdAttribute attribute = typeof(Program).Assembly
            .GetCustomAttribute<UserSecretsIdAttribute>();

        Assert.NotNull(attribute);
        Assert.StartsWith("Somtoday2MicrosoftSDS-", attribute.UserSecretsId, StringComparison.Ordinal);
    }

    [Fact]
    public void LocationSelectionIncludesAllWhenIncludeListIsEmptyAndAppliesExclusions()
    {
        List<Vestiging> locations =
        [
            Location("A"),
            Location(" b "),
            Location("C")
        ];

        List<Vestiging> selected = LocationSelector.Select(locations, [], [" B "]);

        Assert.Equal(new[] { "A", "C" }, selected.Select(location => location.Afkorting));
    }

    [Fact]
    public void LocationSelectionUsesCaseInsensitiveWhitelistAndExclusionWins()
    {
        List<Vestiging> locations = [Location("A"), Location("B"), Location("C")];

        List<Vestiging> selected = LocationSelector.Select(locations, [" a ", "B", "unknown"], ["A"]);

        Assert.Single(selected);
        Assert.Equal("B", selected[0].Afkorting);
    }

    [Fact]
    public void LocationSelectionRetainsBlankCodesOnlyWhenInclusionListIsEmpty()
    {
        List<Vestiging> locations = [Location(null), Location(" "), Location("A")];

        List<Vestiging> selectedWithoutWhitelist = LocationSelector.Select(locations, [], []);
        List<Vestiging> selectedWithWhitelist = LocationSelector.Select(locations, ["A"], []);

        Assert.Equal(locations, selectedWithoutWhitelist);
        Assert.Equal("A", Assert.Single(selectedWithWhitelist).Afkorting);
    }

    [Theory]
    [InlineData("AC/HL", "AC_HL")]
    [InlineData("AC\\HL", "AC_HL")]
    [InlineData(" school ", "school")]
    public void BlobPathSegmentsAreSanitized(string input, string expected)
    {
        Assert.Equal(expected, BlobPathHelper.SanitizeSegment(input, "abbreviation"));
    }

    [Fact]
    public void BlobPrefixUsesForwardSlashesAndVersionBelowLocation()
    {
        string output = BlobPathHelper.NormalizePrefix("/sds\\output/");
        string v1Prefix = BlobPathHelper.Combine(output, "school", "location", "v1");
        string v2Prefix = BlobPathHelper.Combine(output, "school", "location", "v2");

        Assert.Equal("sds/output/school/location/v1", v1Prefix);
        Assert.Equal("sds/output/school/location/v2", v2Prefix);
    }

    [Fact]
    public void InstitutionSelectionMatchesConfiguredUuidFromMultiInstitutionResponse()
    {
        Instelling[] institutions =
        [
            Institution(FirstSchoolUuid, "FIRST"),
            Institution(SecondSchoolUuid, "SECOND")
        ];

        Instelling selected = OpenAPIHelper.SelectInstitution(institutions, SecondSchoolUuid);

        Assert.Equal("SECOND", selected.Afkorting);
    }

    [Fact]
    public void InstitutionSelectionRejectsMissingDuplicateOrEmptyAbbreviation()
    {
        Assert.Throws<InvalidOperationException>(() => OpenAPIHelper.SelectInstitution([], FirstSchoolUuid));
        Assert.Throws<InvalidOperationException>(() => OpenAPIHelper.SelectInstitution(
            [Institution(FirstSchoolUuid, "A"), Institution(FirstSchoolUuid, "B")],
            FirstSchoolUuid));
        Assert.Throws<InvalidOperationException>(() => OpenAPIHelper.SelectInstitution(
            [Institution(FirstSchoolUuid, " ")],
            FirstSchoolUuid));
        Assert.Throws<InvalidOperationException>(() => OpenAPIHelper.SelectInstitution(
            [Institution(FirstSchoolUuid, "..")],
            FirstSchoolUuid));
    }

    [Theory]
    [InlineData("--empty-csv", 1, 1, true)]
    [InlineData("", 7, 31, true)]
    [InlineData("", 7, 30, false)]
    public void EmptyCsvModeUsesArgumentOrFixedYearEnd(string argument, int month, int day, bool expected)
    {
        string[] args = string.IsNullOrEmpty(argument) ? [] : [argument];
        Assert.Equal(expected, Program.ShouldGenerateEmptyCsv(args, false, new DateOnly(2026, month, day)));
    }

    [Fact]
    public void EmptyCsvArgumentIsExactCaseInsensitiveAndAllowsDuplicates()
    {
        DateOnly ordinaryDay = new(2026, 1, 1);

        Assert.True(Program.ShouldGenerateEmptyCsv(
            ["--ignored", "--EMPTY-CSV", "--empty-csv"],
            configuredGenerateEmptyCsv: false,
            ordinaryDay));
        Assert.False(Program.ShouldGenerateEmptyCsv(
            ["--empty-csv=true", "--Output:Folder=override", "unrelated"],
            configuredGenerateEmptyCsv: false,
            ordinaryDay));
    }

    [Fact]
    public void HostConfigurationDoesNotRegisterACommandLineProvider()
    {
        HostApplicationBuilder builder = Program.CreateHostApplicationBuilder();

        Assert.DoesNotContain(
            builder.Configuration.Sources,
            source => source.GetType().FullName?.Contains(
                "CommandLine",
                StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void RunIdIsACompactVersion7Guid()
    {
        string runId = Program.CreateRunId();

        Assert.Equal(32, runId.Length);
        Assert.True(Guid.TryParseExact(runId, "N", out _));
        Assert.Equal('7', runId[12]);
    }

    [Theory]
    [InlineData(2026, 7, 30, 22, 30, 2026, 7, 31)]
    [InlineData(2026, 12, 31, 23, 30, 2027, 1, 1)]
    public void AmsterdamDateUsesCetAndCestAtLocalMidnight(
        int utcYear,
        int utcMonth,
        int utcDay,
        int utcHour,
        int utcMinute,
        int expectedYear,
        int expectedMonth,
        int expectedDay)
    {
        DateTimeOffset instant = new(
            utcYear,
            utcMonth,
            utcDay,
            utcHour,
            utcMinute,
            0,
            TimeSpan.Zero);

        Assert.Equal(
            new DateOnly(expectedYear, expectedMonth, expectedDay),
            AmsterdamTimeHelper.GetDate(instant));
    }

    [Fact]
    public void AmsterdamSchoolYearChangesAtLocalAugustFirst()
    {
        DateOnly beforeBoundary = AmsterdamTimeHelper.GetDate(
            new DateTimeOffset(2026, 7, 31, 21, 59, 0, TimeSpan.Zero));
        DateOnly atBoundary = AmsterdamTimeHelper.GetDate(
            new DateTimeOffset(2026, 7, 31, 22, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 7, 31), beforeBoundary);
        Assert.Equal("2025-2026", AmsterdamTimeHelper.GetSchoolYear(beforeBoundary));
        Assert.Equal(new DateOnly(2026, 8, 1), atBoundary);
        Assert.Equal("2026-2027", AmsterdamTimeHelper.GetSchoolYear(atBoundary));
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string> overrides)
    {
        Dictionary<string, string> values = new()
        {
            ["Somtoday:Environment"] = "PROD",
            ["Somtoday:ClientId"] = "client-id",
            ["Storage:AzureBlob:ConnectionString"] = "UseDevelopmentStorage=true",
            ["Storage:AzureBlob:Container"] = "sds",
            ["Output:Folder"] = "sds/output",
            ["Output:GenerateEmptyCsv"] = "false",
            ["SchoolDataSync:EnableGuardianSync"] = "false"
        };

        foreach ((string key, string value) in overrides)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static Vestiging Location(string abbreviation)
    {
        return new Vestiging
        {
            Uuid = Guid.NewGuid(),
            Naam = abbreviation,
            Afkorting = abbreviation
        };
    }

    private static Instelling Institution(Guid uuid, string abbreviation)
    {
        return new Instelling
        {
            Uuid = uuid,
            Naam = abbreviation,
            Afkorting = abbreviation,
            Brins = []
        };
    }
}

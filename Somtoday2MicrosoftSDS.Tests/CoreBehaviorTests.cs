using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Somtoday2MicrosoftSDS.Helpers;
using Somtoday2MicrosoftSDS.Models;
using Xunit;

namespace Somtoday2MicrosoftSDS.Tests;

public sealed class CoreBehaviorTests
{
    private static readonly Guid SchoolId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FlowId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void TrackedDefaultsContainNoCredentialsAndFailClosed()
    {
        using FileStream settings = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        IConfiguration configuration = new ConfigurationBuilder().AddJsonStream(settings).Build();

        Assert.Empty(configuration.GetSection("Somtoday:SchoolUUID").GetChildren());
        Assert.True(string.IsNullOrEmpty(configuration["Somtoday:ClientSecret"]));
        Assert.True(string.IsNullOrEmpty(configuration["SchoolDataSync:InboundFlowId"]));
        Assert.Null(configuration["Storage:AzureBlob:ServiceUri"]);
        Assert.False(SyncConfiguration.TryCreate(configuration, false, out _, out _));
    }

    [Fact]
    public void ConfigurationAcceptsMultipleUniqueSchoolsAndOneInboundFlow()
    {
        Guid secondSchool = Guid.Parse("33333333-3333-3333-3333-333333333333");
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string>
        {
            ["Somtoday:SchoolUUID:0"] = SchoolId.ToString(),
            ["Somtoday:SchoolUUID:1"] = secondSchool.ToString()
        });

        Assert.True(SyncConfiguration.TryCreate(configuration, false, out SyncConfiguration result, out string[] errors),
            string.Join(Environment.NewLine, errors));
        Assert.Equal([SchoolId, secondSchool], result.SchoolUuids);
        Assert.Equal(FlowId, result.InboundFlowId);
    }

    [Fact]
    public void ConfigurationRejectsMissingInvalidOrDuplicateIdentifiers()
    {
        IConfiguration missingFlow = CreateConfiguration(new Dictionary<string, string>
        {
            ["SchoolDataSync:InboundFlowId"] = ""
        });
        IConfiguration duplicateSchool = CreateConfiguration(new Dictionary<string, string>
        {
            ["Somtoday:SchoolUUID:1"] = SchoolId.ToString()
        });

        Assert.False(SyncConfiguration.TryCreate(missingFlow, true, out _, out string[] flowErrors));
        Assert.Contains(flowErrors, error => error.Contains("InboundFlowId", StringComparison.Ordinal));
        Assert.False(SyncConfiguration.TryCreate(duplicateSchool, true, out _, out _));
    }

    [Fact]
    public void ConfigurationRejectsEmptyResourceIdentifiers()
    {
        IConfiguration emptySchool = CreateConfiguration(new Dictionary<string, string>
        {
            ["Somtoday:SchoolUUID:0"] = Guid.Empty.ToString()
        });
        IConfiguration emptyFlow = CreateConfiguration(new Dictionary<string, string>
        {
            ["SchoolDataSync:InboundFlowId"] = Guid.Empty.ToString()
        });

        Assert.False(SyncConfiguration.TryCreate(emptySchool, true, out _, out string[] schoolErrors));
        Assert.Contains(schoolErrors, error => error.Contains("empty UUID", StringComparison.Ordinal));
        Assert.False(SyncConfiguration.TryCreate(emptyFlow, true, out _, out string[] flowErrors));
        Assert.Contains(flowErrors, error => error.Contains("non-empty UUID", StringComparison.Ordinal));
    }

    [Fact]
    public void RemovedBlobAndConnectorIdSettingsAreNotPartOfConfiguration()
    {
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string>
        {
            ["Storage:AzureBlob:ServiceUri"] = "http://ignored.invalid",
            ["SchoolDataSync:ConnectorId"] = Guid.NewGuid().ToString()
        });

        Assert.True(SyncConfiguration.TryCreate(configuration, false, out _, out string[] errors),
            string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void NightlyRemainsDevelopmentOnly()
    {
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string>
        {
            ["Somtoday:Environment"] = "NIGHTLY"
        });

        Assert.False(SyncConfiguration.TryCreate(configuration, false, out _, out _));
        Assert.True(SyncConfiguration.TryCreate(configuration, true, out _, out string[] errors),
            string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void EnvironmentSelectionIntentionallyUsesTheFirstNonWhitespaceCharacter()
    {
        (string Configured, SomEnvironmentConfig Expected)[] cases =
        [
            (" P-readable-production ", SomEnvironmentConfig.Prod),
            ("t-local-alias", SomEnvironmentConfig.Test),
            ("A-acceptance", SomEnvironmentConfig.Acceptatie),
            ("n-development-only", SomEnvironmentConfig.Nightly)
        ];

        foreach ((string configured, SomEnvironmentConfig expected) in cases)
        {
            IConfiguration configuration = CreateConfiguration(new Dictionary<string, string>
            {
                ["Somtoday:Environment"] = configured
            });

            Assert.True(SyncConfiguration.TryCreate(configuration, true, out SyncConfiguration result, out string[] errors),
                string.Join(Environment.NewLine, errors));
            Assert.Same(expected, result.SomEnvironment);
        }
    }

    [Fact]
    public void ConnectorFormatSelectsExactlyOneDataset()
    {
        FileHelper helper = new();

        PublicationDataset v1 = Program.CreateEmptyDataset(SdsDatasetFormat.V1, false, helper);
        PublicationDataset v21 = Program.CreateEmptyDataset(SdsDatasetFormat.V2Rev1, false, helper);

        Assert.Equal(SdsDatasetFormat.V1, v1.Format);
        Assert.Equal(6, v1.Files.Count);
        Assert.Equal(SdsDatasetFormat.V2Rev1, v21.Format);
        Assert.Equal(5, v21.Files.Count);
    }

    [Theory]
    [InlineData(7, 31, true)]
    [InlineData(7, 30, false)]
    public void EmptyCsvModeUsesOnlyFixedYearEnd(int month, int day, bool expected)
    {
        Assert.Equal(expected, Program.ShouldUseHeaderOnlyMode(new DateOnly(2026, month, day)));
    }

    [Fact]
    public void HostConfigurationDoesNotRegisterCommandLineConfiguration()
    {
        HostApplicationBuilder builder = Program.CreateHostApplicationBuilder();
        Assert.DoesNotContain(
            builder.Configuration.Sources,
            source => source.GetType().FullName?.Contains("CommandLine", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void SdsHttpClientsDisableAutomaticRedirects()
    {
        using SocketsHttpHandler graphHandler = Program.CreateNoRedirectHandler();
        using SocketsHttpHandler uploadHandler = Program.CreateNoRedirectHandler();

        Assert.False(graphHandler.AllowAutoRedirect);
        Assert.False(uploadHandler.AllowAutoRedirect);
    }

    [Fact]
    public void LocationWithoutExportableClassesIsSkipped()
    {
        ResolvedExportPopulation population = new(
            new Vestiging { Uuid = Guid.NewGuid(), Naam = "Location", Afkorting = "LOC" },
            [], [], [], [], 0);

        Assert.False(Program.ShouldPublishLocation(population, "School", NullLogger<Program>.Instance));
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string> overrides)
    {
        Dictionary<string, string> values = new()
        {
            ["Somtoday:Environment"] = "PROD",
            ["Somtoday:ClientId"] = "client-id",
            ["Somtoday:ClientSecret"] = "client-secret",
            ["Somtoday:SchoolUUID:0"] = SchoolId.ToString(),
            ["SchoolDataSync:InboundFlowId"] = FlowId.ToString(),
            ["SchoolDataSync:EnableGuardianSync"] = "false"
        };
        foreach ((string key, string value) in overrides)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}

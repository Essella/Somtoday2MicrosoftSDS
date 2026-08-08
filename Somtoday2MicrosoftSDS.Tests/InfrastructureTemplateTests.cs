using System.Text.Json;
using Xunit;

namespace Somtoday2MicrosoftSDS.Tests;

public sealed class InfrastructureTemplateTests
{
    [Fact]
    public void BicepUsesTaggedEnvironmentAndOneSystemIdentityJob()
    {
        string root = FindRepositoryRoot();
        string main = File.ReadAllText(Path.Combine(root, "infra", "main.bicep"));
        string additionalJob = File.ReadAllText(Path.Combine(root, "infra", "additional-job.bicep"));
        string job = File.ReadAllText(Path.Combine(root, "infra", "job.bicep"));

        Assert.Contains("param environmentName string", main, StringComparison.Ordinal);
        Assert.Contains("resource installationTag 'Microsoft.Resources/tags", main, StringComparison.Ordinal);
        Assert.Contains("Somtoday2MicrosoftSDS.environment", main, StringComparison.Ordinal);
        Assert.Contains("resourceGroup().tags", additionalJob, StringComparison.Ordinal);
        Assert.DoesNotContain("environmentMode", main + additionalJob, StringComparison.Ordinal);
        Assert.DoesNotContain("existingContainerAppsEnvironmentResourceId", main + additionalJob, StringComparison.Ordinal);
        Assert.Equal(1, Count(job, "resource job 'Microsoft.App/jobs"));
        Assert.Contains("type: 'SystemAssigned'", job, StringComparison.Ordinal);
        Assert.DoesNotContain("UserAssigned", main + additionalJob + job, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.Storage/", main + additionalJob + job, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SharedSyncJobAssignsOnlyRequiredGraphRolesAndNormalizesLocationCodes()
    {
        string syncJob = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "infra", "sync-job.bicep"));

        Assert.Equal(1, Count(syncJob, "'IndustryData-InboundFlow.ReadWrite.All'"));
        Assert.Equal(1, Count(syncJob, "'IndustryData-DataConnector.Upload'"));
        Assert.Equal(1, Count(syncJob, "'IndustryData.ReadBasic.All'"));
        Assert.DoesNotContain("appRoleId: '", syncJob, StringComparison.Ordinal);
        Assert.Contains("filter(normalizedIncludedLocationCodes", syncJob, StringComparison.Ordinal);
        Assert.Contains("filter(normalizedExcludedLocationCodes", syncJob, StringComparison.Ordinal);
        Assert.Contains("var cronMinute =", syncJob, StringComparison.Ordinal);
        Assert.Contains("var imageReference = 'ghcr.io/essella/somtoday2microsoftsds:latest'", syncJob, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedArmTemplatesExposeTheNewDeploymentContracts()
    {
        string root = FindRepositoryRoot();
        using JsonDocument mainDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "infra", "azuredeploy.json")));
        using JsonDocument additionalJobDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "infra", "azuredeploy-additional-job.json")));

        JsonElement mainParameters = mainDocument.RootElement.GetProperty("parameters");
        JsonElement additionalJobParameters = additionalJobDocument.RootElement.GetProperty("parameters");

        Assert.Equal("securestring", mainParameters.GetProperty("somtodayClientSecret").GetProperty("type").GetString());
        Assert.True(mainParameters.TryGetProperty("environmentName", out _));
        Assert.True(mainParameters.TryGetProperty("jobPrefix", out _));
        Assert.True(mainParameters.TryGetProperty("schoolUuidsCsv", out _));
        Assert.False(mainParameters.TryGetProperty("environmentMode", out _));
        Assert.False(mainParameters.TryGetProperty("existingContainerAppsEnvironmentResourceId", out _));
        Assert.False(mainParameters.TryGetProperty("cronExpression", out _));

        Assert.Equal("securestring", additionalJobParameters.GetProperty("somtodayClientSecret").GetProperty("type").GetString());
        Assert.True(additionalJobParameters.TryGetProperty("jobPrefix", out _));
        Assert.True(additionalJobParameters.TryGetProperty("schoolUuidsCsv", out _));
        Assert.False(additionalJobParameters.TryGetProperty("environmentName", out _));
        Assert.False(additionalJobParameters.TryGetProperty("environmentMode", out _));
    }

    [Fact]
    public void InfrastructureUsesTheGraphExtensionAndSeparateAdditionalJobEntrypoint()
    {
        string root = FindRepositoryRoot();
        string configuration = File.ReadAllText(Path.Combine(root, "infra", "bicepconfig.json"));
        string additionalJobExample = File.ReadAllText(Path.Combine(root, "infra", "additional-job.example.bicepparam"));

        Assert.Contains("microsoftGraphV1", configuration, StringComparison.Ordinal);
        Assert.Contains("using './additional-job.bicep'", additionalJobExample, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "infra", "additional-job.bicep")));
        Assert.True(File.Exists(Path.Combine(root, "infra", "azuredeploy-additional-job.json")));
        Assert.False(File.Exists(Path.Combine(root, "infra", "uiFormDefinition.json")));
    }

    private static int Count(string text, string value)
    {
        return text.Split(value, StringSplitOptions.None).Length - 1;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Somtoday2MicrosoftSDS.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found");
    }
}

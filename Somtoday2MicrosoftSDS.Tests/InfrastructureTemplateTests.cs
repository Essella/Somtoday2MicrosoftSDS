using System.Text.Json;
using Xunit;

namespace Somtoday2MicrosoftSDS.Tests;

public sealed class InfrastructureTemplateTests
{
    [Fact]
    public void EnvironmentTemplateCreatesOnlyAzureEnvironmentResources()
    {
        string main = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "infra", "main.bicep"));

        Assert.Contains("param environmentName string", main, StringComparison.Ordinal);
        Assert.Contains("param logAnalyticsName string", main, StringComparison.Ordinal);
        Assert.Contains("resource installationTag 'Microsoft.Resources/tags", main, StringComparison.Ordinal);
        Assert.Contains("Somtoday2MicrosoftSDS.environment", main, StringComparison.Ordinal);
        Assert.DoesNotContain("module ", main, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Graph/", main, StringComparison.Ordinal);
        Assert.DoesNotContain("somtodayClientSecret", main, StringComparison.Ordinal);
    }

    [Fact]
    public void DeploySyncJobUsesTaggedEnvironmentAndOneSystemIdentityJob()
    {
        string root = FindRepositoryRoot();
        string deploySyncJob = File.ReadAllText(Path.Combine(root, "infra", "deploy-sync-job.bicep"));
        string job = File.ReadAllText(Path.Combine(root, "infra", "job.bicep"));

        Assert.Contains("resourceGroup().tags", deploySyncJob, StringComparison.Ordinal);
        Assert.Contains("module syncJob './sync-job.bicep'", deploySyncJob, StringComparison.Ordinal);
        Assert.Equal(1, Count(job, "resource job 'Microsoft.App/jobs"));
        Assert.Contains("type: 'SystemAssigned'", job, StringComparison.Ordinal);
        Assert.DoesNotContain("UserAssigned", deploySyncJob + job, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.Storage/", deploySyncJob + job, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SharedSyncJobNormalizesInputsAndKeepsTheDeploymentAzureOnly()
    {
        string syncJob = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "infra", "sync-job.bicep"));

        Assert.DoesNotContain("Microsoft.Graph/", syncJob, StringComparison.Ordinal);
        Assert.Contains("replace(value, '\"', '')", syncJob, StringComparison.Ordinal);
        Assert.Contains("filter(normalizedIncludedLocationCodes", syncJob, StringComparison.Ordinal);
        Assert.Contains("filter(normalizedExcludedLocationCodes", syncJob, StringComparison.Ordinal);
        Assert.Contains("var cronMinute =", syncJob, StringComparison.Ordinal);
        Assert.Contains("var imageReference = 'ghcr.io/essella/somtoday2microsoftsds:latest'", syncJob, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedEnvironmentArmTemplateExposesOnlyEnvironmentParameters()
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(FindRepositoryRoot(), "infra", "azuredeploy.json")));
        JsonElement parameters = document.RootElement.GetProperty("parameters");

        Assert.Equal(2, parameters.EnumerateObject().Count());
        Assert.True(parameters.TryGetProperty("environmentName", out _));
        Assert.True(parameters.TryGetProperty("logAnalyticsName", out _));
        Assert.False(parameters.TryGetProperty("somtodayClientSecret", out _));
        Assert.False(parameters.TryGetProperty("jobPrefix", out _));
        Assert.False(parameters.TryGetProperty("schoolUuidsCsv", out _));
        Assert.False(parameters.TryGetProperty("environmentMode", out _));
    }

    [Fact]
    public void GeneratedSyncJobArmTemplateExposesTheJobParameters()
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(FindRepositoryRoot(), "infra", "azuredeploy-sync-job.json")));
        JsonElement parameters = document.RootElement.GetProperty("parameters");

        Assert.True(parameters.TryGetProperty("jobPrefix", out _));
        Assert.True(parameters.TryGetProperty("schoolUuidsCsv", out _));
        Assert.True(parameters.TryGetProperty("inboundFlowId", out _));
        Assert.True(parameters.TryGetProperty("somtodayClientSecret", out JsonElement secret));
        Assert.Equal("securestring", secret.GetProperty("type").GetString());
        Assert.False(parameters.TryGetProperty("environmentName", out _));
        Assert.False(parameters.TryGetProperty("containerAppsEnvironmentName", out _));
    }

    [Fact]
    public void InfrastructureUsesTheBulkRoleAssignmentScript()
    {
        string root = FindRepositoryRoot();
        string script = File.ReadAllText(Path.Combine(root, "infra", "assign-sync-job-roles.ps1"));
        string jobExample = File.ReadAllText(Path.Combine(root, "infra", "deploy-sync-job.example.bicepparam"));

        Assert.Contains("Connect-MgGraph", script, StringComparison.Ordinal);
        Assert.Contains("'Application.Read.All'", script, StringComparison.Ordinal);
        Assert.Contains("'AppRoleAssignment.ReadWrite.All'", script, StringComparison.Ordinal);
        Assert.Equal(1, Count(script, "'IndustryData-InboundFlow.ReadWrite.All'"));
        Assert.Equal(1, Count(script, "'IndustryData-DataConnector.Upload'"));
        Assert.Equal(1, Count(script, "'IndustryData.ReadBasic.All'"));
        Assert.Contains("using './deploy-sync-job.bicep'", jobExample, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "infra", "deploy-sync-job.bicep")));
        Assert.True(File.Exists(Path.Combine(root, "infra", "azuredeploy-sync-job.json")));
        Assert.False(File.Exists(Path.Combine(root, "infra", "deploy-sync-job.ps1")));
        Assert.False(File.Exists(Path.Combine(root, "infra", "bicepconfig.json")));
        Assert.False(File.Exists(Path.Combine(root, "infra", "additional-job.bicep")));
        Assert.False(File.Exists(Path.Combine(root, "infra", "azuredeploy-additional-job.json")));
        Assert.False(File.Exists(Path.Combine(root, "infra", "uiFormDefinition.json")));
    }

    [Fact]
    public void CloudShellRoleAssignmentScriptFindsOnlyTaggedJobs()
    {
        string script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "infra", "assign-sync-job-roles.ps1"));

        Assert.Contains("Somtoday2MicrosoftSDS.environment", script, StringComparison.Ordinal);
        Assert.Contains("Somtoday2MicrosoftSDS.instance", script, StringComparison.Ordinal);
        Assert.Contains("'group', 'list'", script, StringComparison.Ordinal);
        Assert.Contains("'containerapp', 'job', 'list'", script, StringComparison.Ordinal);
        Assert.Contains("Grant-JobGraphRoles -Jobs $jobs", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Host $env:SOMTODAY_CLIENT_SECRET", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Read-Host", script, StringComparison.Ordinal);
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

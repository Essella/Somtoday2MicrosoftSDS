using System.Text.Json;
using Xunit;

namespace Somtoday2MicrosoftSDS.Tests;

public sealed class InfrastructureTemplateTests
{
    [Fact]
    public void BicepCreatesOneSystemIdentityJobAndNoStorage()
    {
        string root = FindRepositoryRoot();
        string main = File.ReadAllText(Path.Combine(root, "infra", "main.bicep"));
        string job = File.ReadAllText(Path.Combine(root, "infra", "job.bicep"));

        Assert.Contains("param environmentMode string = 'existing'", main, StringComparison.Ordinal);
        Assert.Contains("existingContainerAppsEnvironmentResourceId", main, StringComparison.Ordinal);
        Assert.Equal(1, Count(job, "resource job 'Microsoft.App/jobs"));
        Assert.Contains("type: 'SystemAssigned'", job, StringComparison.Ordinal);
        Assert.DoesNotContain("UserAssigned", main + job, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.Storage/", main + job, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scheduledJob.outputs.principalId", main, StringComparison.Ordinal);
    }

    [Fact]
    public void BicepAssignsOnlyRequiredGraphRolesByValue()
    {
        string main = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "infra", "main.bicep"));

        Assert.Equal(1, Count(main, "'IndustryData-InboundFlow.ReadWrite.All'"));
        Assert.Equal(1, Count(main, "'IndustryData-DataConnector.Upload'"));
        Assert.Equal(1, Count(main, "'IndustryData.ReadBasic.All'"));
        Assert.DoesNotContain("appRoleId: '", main, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedArmMatchesPublicConfigurationShape()
    {
        string root = FindRepositoryRoot();
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "infra", "azuredeploy.json")));
        JsonElement parameters = document.RootElement.GetProperty("parameters");

        Assert.Equal("securestring", parameters.GetProperty("somtodayClientSecret").GetProperty("type").GetString());
        Assert.True(parameters.TryGetProperty("inboundFlowId", out _));
        Assert.True(parameters.TryGetProperty("schoolUuids", out _));
        Assert.True(parameters.TryGetProperty("location", out _));
        Assert.True(parameters.TryGetProperty("environmentMode", out _));
        Assert.True(parameters.TryGetProperty("existingContainerAppsEnvironmentResourceId", out _));
        Assert.False(parameters.GetProperty("schoolUuids").TryGetProperty("defaultValue", out _));
        Assert.False(parameters.TryGetProperty("blobContainerName", out _));
        Assert.False(parameters.TryGetProperty("outputFolder", out _));
    }

    [Fact]
    public void PortalFormSelectsAnExistingEnvironmentAndMapsEveryTemplateParameter()
    {
        string root = FindRepositoryRoot();
        using JsonDocument formDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "infra", "uiFormDefinition.json")));
        using JsonDocument templateDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "infra", "azuredeploy.json")));

        JsonElement view = formDocument.RootElement.GetProperty("view");
        JsonElement steps = view.GetProperty("properties").GetProperty("steps");
        JsonElement environmentStep = steps.EnumerateArray()
            .Single(step => step.GetProperty("name").GetString() == "environment");
        JsonElement elements = environmentStep.GetProperty("elements");

        JsonElement environmentMode = elements.EnumerateArray()
            .Single(element => element.GetProperty("name").GetString() == "environmentMode");
        Assert.Equal("existing", environmentMode.GetProperty("defaultValue").GetProperty("value").GetString());

        JsonElement environmentSelector = elements.EnumerateArray()
            .Single(element => element.GetProperty("name").GetString() == "existingEnvironment");
        Assert.Equal("Microsoft.Solutions.ResourceSelector", environmentSelector.GetProperty("type").GetString());
        Assert.Equal("Microsoft.App/managedEnvironments", environmentSelector.GetProperty("resourceType").GetString());
        Assert.Contains("resourceScope.subscription.subscriptionId",
            environmentSelector.GetProperty("scope").GetProperty("subscriptionId").GetString(),
            StringComparison.Ordinal);

        HashSet<string> formParameters = view.GetProperty("outputs").GetProperty("parameters")
            .EnumerateObject()
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> templateParameters = templateDocument.RootElement.GetProperty("parameters")
            .EnumerateObject()
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            templateParameters.SetEquals(formParameters),
            $"Form parameters differ from ARM parameters. Form-only: {string.Join(", ", formParameters.Except(templateParameters))}. ARM-only: {string.Join(", ", templateParameters.Except(formParameters))}.");
        Assert.Contains("existingContainerAppsEnvironmentResourceId", formParameters);
        Assert.Contains("somtodayClientSecret", formParameters);
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

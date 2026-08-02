using Microsoft.Extensions.Logging.Abstractions;
using Somtoday2MicrosoftSDS.Helpers;
using Xunit;

namespace Somtoday2MicrosoftSDS.Tests;

[Trait("Category", "SomtodayIntegration")]
public sealed class SomtodayOpenApiIntegrationTests : IClassFixture<SomtodayOpenApiFixture>
{
    private readonly SomtodayOpenApiFixture fixture;

    public SomtodayOpenApiIntegrationTests(SomtodayOpenApiFixture fixture)
    {
        this.fixture = fixture;
    }

    [SomtodayIntegrationFact("POST /oauth2/token?organisation={schoolUuid} authenticates")]
    public void OAuthTokenEndpointAcceptsConfiguredCredentials()
    {
        Assert.True(fixture.Api.IsConnected);
    }

    [SomtodayIntegrationFact("Unauthenticated production GET /rest/v1/connect/instelling returns the configured institution")]
    public async Task ConfiguredInstitutionCanBeRetrieved()
    {
        Instelling institution = await fixture.ExecuteSafelyAsync(
            cancellationToken => fixture.Api.GetInstellingAsync(cancellationToken));

        Assert.Equal(fixture.SchoolUuid, institution.Uuid);
        Assert.False(string.IsNullOrWhiteSpace(institution.Naam));
        Assert.False(string.IsNullOrWhiteSpace(institution.Afkorting));
    }

    [SomtodayIntegrationFact("GET /rest/v1/connect/vestiging returns permitted locations")]
    public async Task PermittedLocationsCanBeRetrieved()
    {
        List<Vestiging> locations = await fixture.GetLocationsAsync();

        Assert.NotEmpty(locations);
        Assert.All(locations, location =>
        {
            Assert.NotEqual(Guid.Empty, location.Uuid);
            Assert.False(string.IsNullOrWhiteSpace(location.Naam));
            Assert.False(string.IsNullOrWhiteSpace(location.Afkorting));
        });
        Assert.True(
            locations.Count == locations.Select(location => location.Uuid).Distinct().Count(),
            "Somtoday returned duplicate location UUIDs.");
    }

    [SomtodayIntegrationFact(
        "GET /rest/v1/connect/vestiging/{vestigingUuid}/lesgroep/ returns HUIDIG groups with pagination")]
    public Task CurrentGroupsCanBeRetrievedForEveryPermittedLocation()
    {
        return AssertEndpointForEveryLocationAsync(
            (location, cancellationToken) => fixture.Api.GetLesgroepenAsync(location, cancellationToken),
            group => group.Uuid);
    }

    [SomtodayIntegrationFact(
        "GET /rest/v1/connect/vestiging/{vestigingUuid}/medewerker returns HUIDIG employees with pagination")]
    public Task CurrentEmployeesCanBeRetrievedForEveryPermittedLocation()
    {
        return AssertEndpointForEveryLocationAsync(
            (location, cancellationToken) => fixture.Api.GetTeacherInfoAsync(location, cancellationToken),
            employee => employee.Uuid);
    }

    [SomtodayIntegrationFact(
        "GET /rest/v1/connect/vestiging/{vestigingUuid}/leerling returns HUIDIG pupils with pagination")]
    public Task CurrentPupilsCanBeRetrievedForEveryPermittedLocation()
    {
        return AssertEndpointForEveryLocationAsync(
            (location, cancellationToken) => fixture.Api.GetStudentInfoAsync(location, cancellationToken),
            pupil => pupil.Uuid);
    }

    [SomtodayIntegrationFact(
        "GET /rest/v1/connect/vestiging/{vestigingUuid}/ouderVerzorger/ returns HUIDIG guardians with pagination",
        requiresGuardians: true)]
    public Task CurrentGuardiansCanBeRetrievedForEveryPermittedLocation()
    {
        return AssertEndpointForEveryLocationAsync(
            (location, cancellationToken) => fixture.Api.GetGuardianInfoAsync(location, cancellationToken),
            guardian => guardian.Uuid);
    }

    private async Task AssertEndpointForEveryLocationAsync<T>(
        Func<Vestiging, CancellationToken, Task<List<T>>> request,
        Func<T, Guid> selectUuid)
    {
        List<Vestiging> locations = await fixture.GetLocationsAsync();

        foreach (Vestiging location in locations)
        {
            List<T> entities = await fixture.ExecuteSafelyAsync(
                cancellationToken => request(location, cancellationToken));
            AssertUniqueNonEmptyUuids(entities.Select(selectUuid));
        }
    }

    private static void AssertUniqueNonEmptyUuids(IEnumerable<Guid> uuids)
    {
        Guid[] values = uuids.ToArray();
        Assert.False(values.Any(uuid => uuid == Guid.Empty), "Somtoday returned an empty entity UUID.");
        Assert.True(
            values.Length == values.Distinct().Count(),
            "Somtoday returned duplicate entity UUIDs while paging through current data.");
    }
}

public sealed class SomtodayIntegrationFactAttribute : FactAttribute
{
    public SomtodayIntegrationFactAttribute(
        string displayName,
        bool requiresGuardians = false,
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = null,
        [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        DisplayName = displayName;

        if (!bool.TryParse(
                Environment.GetEnvironmentVariable(SomtodayOpenApiFixture.EnabledVariable),
                out bool enabled)
            || !enabled)
        {
            Skip = "Live Somtoday integration tests are opt-in; use scripts/Test-SomtodayOpenApi.ps1.";
        }
        else if (requiresGuardians
            && (!bool.TryParse(
                    Environment.GetEnvironmentVariable(SomtodayOpenApiFixture.IncludeGuardiansVariable),
                    out bool includeGuardians)
                || !includeGuardians))
        {
            Skip = "Guardian endpoint coverage is opt-in; add -IncludeGuardians to the test runner.";
        }
    }
}

public sealed class SomtodayOpenApiFixture : IAsyncLifetime
{
    internal const string EnabledVariable = "SOMTODAY_INTEGRATION_TESTS";
    private const string SchoolUuidVariable = "SOMTODAY_INTEGRATION_SCHOOL_UUID";
    private const string ClientIdVariable = "SOMTODAY_INTEGRATION_CLIENT_ID";
    private const string ClientSecretVariable = "SOMTODAY_INTEGRATION_CLIENT_SECRET";
    internal const string IncludeGuardiansVariable = "SOMTODAY_INTEGRATION_INCLUDE_GUARDIANS";
    private static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(10);

    private IntegrationHttpClientFactory httpClientFactory;
    private Task<List<Vestiging>> locationsTask;

    internal Guid SchoolUuid { get; private set; }

    internal OpenAPIHelper Api { get; private set; }

    public async ValueTask InitializeAsync()
    {
        SchoolUuid = ReadSchoolUuid();
        string clientId = ReadRequiredVariable(ClientIdVariable);
        string clientSecret = ReadRequiredVariable(ClientSecretVariable);
        httpClientFactory = new IntegrationHttpClientFactory();
        Api = new OpenAPIHelper(
            clientId,
            clientSecret,
            SchoolUuid,
            SomEnvironmentConfig.Prod,
            httpClientFactory,
            NullLogger<OpenAPIHelper>.Instance);

        await ExecuteSafelyAsync(cancellationToken => Api.ConnectAsync(cancellationToken));
        if (!Api.IsConnected)
        {
            throw new InvalidOperationException(
                "Somtoday production authentication did not succeed; verify the supplied integration-test credentials.");
        }
    }

    public ValueTask DisposeAsync()
    {
        httpClientFactory?.Dispose();
        return ValueTask.CompletedTask;
    }

    internal Task<List<Vestiging>> GetLocationsAsync()
    {
        return locationsTask ??= ExecuteSafelyAsync(
            cancellationToken => Api.GetSelectedVestigingenAsync([], [], cancellationToken));
    }

    internal async Task ExecuteSafelyAsync(Func<CancellationToken, Task> action)
    {
        using CancellationTokenSource timeout = new(CallTimeout);

        try
        {
            await action(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("The Somtoday live OpenAPI call exceeded the ten-minute test timeout.");
        }
        catch (Exception exception)
        {
            throw CreateSafeTestException(exception);
        }
    }

    internal async Task<T> ExecuteSafelyAsync<T>(Func<CancellationToken, Task<T>> action)
    {
        using CancellationTokenSource timeout = new(CallTimeout);

        try
        {
            return await action(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("The Somtoday live OpenAPI call exceeded the ten-minute test timeout.");
        }
        catch (Exception exception)
        {
            throw CreateSafeTestException(exception);
        }
    }

    private static Exception CreateSafeTestException(Exception exception)
    {
        return new InvalidOperationException(
            $"Somtoday live OpenAPI call failed ({SafeExceptionSummary.Create(exception)}). "
            + "The response body was omitted because it can contain personal data.");
    }

    private static Guid ReadSchoolUuid()
    {
        string value = ReadRequiredVariable(SchoolUuidVariable);
        if (!Guid.TryParse(value, out Guid schoolUuid) || schoolUuid == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Environment variable {SchoolUuidVariable} must contain a non-empty UUID.");
        }

        return schoolUuid;
    }

    private static string ReadRequiredVariable(string name)
    {
        string value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Environment variable {name} is required for live Somtoday integration tests.");
        }

        return value;
    }

    private sealed class IntegrationHttpClientFactory : IHttpClientFactory, IDisposable
    {
        private readonly List<HttpClient> clients = [];

        public HttpClient CreateClient(string name)
        {
            HttpClient client = new();
            clients.Add(client);
            return client;
        }

        public void Dispose()
        {
            foreach (HttpClient client in clients)
            {
                client.Dispose();
            }
        }
    }
}

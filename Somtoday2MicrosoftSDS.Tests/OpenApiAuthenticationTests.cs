using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Somtoday2MicrosoftSDS.Helpers;
using Somtoday2MicrosoftSDS.Models;
using Xunit;

namespace Somtoday2MicrosoftSDS.Tests;

public class OpenApiAuthenticationTests
{
    private static readonly Guid SchoolUuid = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task OAuthFormIsEncodedAndAccessTokenIsNeverLogged()
    {
        const string accessToken = "sensitive-access-token";
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"access_token\":\"{accessToken}\"}}", Encoding.UTF8, "application/json")
        });
        CapturingLogger logger = new();
        OpenAPIHelper helper = new(
            "client+id&value",
            "secret&value=1+2",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SomEnvironmentConfig.Prod,
            new RecordingHttpClientFactory(handler),
            logger);

        await helper.ConnectAsync(CancellationToken.None);

        Assert.True(helper.IsConnected);
        Assert.Equal(
            "grant_type=client_credentials&client_id=client%2Bid%26value&client_secret=secret%26value%3D1%2B2",
            handler.RequestBody);
        Assert.DoesNotContain(logger.Messages, message => message.Contains(accessToken, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("secret&value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthenticationFailureLogsOnlyStatusAndNotResponseBody()
    {
        const string sensitiveBody = "secret response with token and personal data";
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(sensitiveBody)
        });
        CapturingLogger logger = new();
        OpenAPIHelper helper = new(
            "client-id",
            "client-secret",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SomEnvironmentConfig.Prod,
            new RecordingHttpClientFactory(handler),
            logger);

        await helper.ConnectAsync(CancellationToken.None);

        Assert.False(helper.IsConnected);
        Assert.Contains(logger.Messages, message => message.Contains("401", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains(sensitiveBody, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("client-secret", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task PermanentClientErrorsAreNotRetried(HttpStatusCode statusCode)
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(statusCode));
        int delayCount = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Program.ConnectWithRetryAsync(
            SchoolUuid,
            CreateSyncConfiguration(),
            new RecordingHttpClientFactory(handler),
            new CapturingLogger(),
            new CapturingProgramLogger(),
            CancellationToken.None,
            (_, _) =>
            {
                delayCount++;
                return Task.CompletedTask;
            }));

        Assert.Single(handler.Requests);
        Assert.Equal(0, delayCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task TransientHttpFailuresUseAtMostFourTotalAttempts(HttpStatusCode statusCode)
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(statusCode));
        int delayCount = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Program.ConnectWithRetryAsync(
            SchoolUuid,
            CreateSyncConfiguration(),
            new RecordingHttpClientFactory(handler),
            new CapturingLogger(),
            new CapturingProgramLogger(),
            CancellationToken.None,
            (delay, _) =>
            {
                Assert.Equal(TimeSpan.FromSeconds(2), delay);
                delayCount++;
                return Task.CompletedTask;
            }));

        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal(3, delayCount);
    }

    [Fact]
    public async Task AuthenticationCanRecoverDuringTransientRetry()
    {
        int responseNumber = 0;
        RecordingHandler handler = new(_ => ++responseNumber == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : JsonResponse("{\"access_token\":\"token\"}"));

        OpenAPIHelper helper = await Program.ConnectWithRetryAsync(
            SchoolUuid,
            CreateSyncConfiguration(),
            new RecordingHttpClientFactory(handler),
            new CapturingLogger(),
            new CapturingProgramLogger(),
            CancellationToken.None,
            (_, _) => Task.CompletedTask);

        Assert.True(helper.IsConnected);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task HttpTimeoutIsTransientAndBounded()
    {
        RecordingHandler handler = new(_ => throw new TaskCanceledException("HTTP timeout"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Program.ConnectWithRetryAsync(
            SchoolUuid,
            CreateSyncConfiguration(),
            new RecordingHttpClientFactory(handler),
            new CapturingLogger(),
            new CapturingProgramLogger(),
            CancellationToken.None,
            (_, _) => Task.CompletedTask));

        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task NetworkFailureIsTransientAndBounded()
    {
        RecordingHandler handler = new(_ => throw new HttpRequestException("network unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Program.ConnectWithRetryAsync(
            SchoolUuid,
            CreateSyncConfiguration(),
            new RecordingHttpClientFactory(handler),
            new CapturingLogger(),
            new CapturingProgramLogger(),
            CancellationToken.None,
            (_, _) => Task.CompletedTask));

        Assert.Equal(4, handler.Requests.Count);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"access_token\":123}")]
    [InlineData("not-json")]
    public async Task InvalidAuthenticationPayloadIsPermanent(string responseBody)
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => Program.ConnectWithRetryAsync(
            SchoolUuid,
            CreateSyncConfiguration(),
            new RecordingHttpClientFactory(handler),
            new CapturingLogger(),
            new CapturingProgramLogger(),
            CancellationToken.None,
            (_, _) => Task.CompletedTask));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CancellationDuringRetryDelayStopsImmediately()
    {
        using CancellationTokenSource cancellation = new();
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Program.ConnectWithRetryAsync(
            SchoolUuid,
            CreateSyncConfiguration(),
            new RecordingHttpClientFactory(handler),
            new CapturingLogger(),
            new CapturingProgramLogger(),
            cancellation.Token,
            (_, token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled(token);
            }));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AuthenticationCancellationIsPropagated()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OpenAPIHelper helper = new(
            "client-id",
            "client-secret",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SomEnvironmentConfig.Prod,
            new RecordingHttpClientFactory(new RecordingHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK))),
            new CapturingLogger());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            helper.ConnectAsync(cancellation.Token));
    }

    [Fact]
    public async Task SchoolApiFailureIsPropagatedWithoutLoggingResponseBody()
    {
        const string sensitiveBody = "student data that must not be logged";
        int requestCount = 0;
        RecordingHandler handler = new(_ =>
        {
            if (Interlocked.Increment(ref requestCount) == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"token\"}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(sensitiveBody)
            };
        });
        CapturingLogger logger = new();
        OpenAPIHelper helper = new(
            "client-id",
            "client-secret",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SomEnvironmentConfig.Prod,
            new RecordingHttpClientFactory(handler),
            logger);
        await helper.ConnectAsync(CancellationToken.None);

        await Assert.ThrowsAsync<ApiException>(() =>
            helper.GetSelectedVestigingenAsync([], [], CancellationToken.None));

        Assert.DoesNotContain(logger.Messages, message => message.Contains(sensitiveBody, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("client-secret", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("TEST")]
    [InlineData("ACCEPTATIE")]
    public async Task InstitutionLookupUsesUnauthenticatedProductionEndpointForNonProductionEnvironment(
        string environment)
    {
        RecordingHandler handler = new(_ => JsonResponse(
            $$"""
            {"instellingen":[{"uuid":"{{SchoolUuid}}","naam":"Public school","afkorting":"PUBLIC","brins":[]}]}
            """));
        OpenAPIHelper helper = new(
            "client-id",
            "client-secret",
            SchoolUuid,
            environment == "TEST" ? SomEnvironmentConfig.Test : SomEnvironmentConfig.Acceptatie,
            new RecordingHttpClientFactory(handler),
            new CapturingLogger());

        Instelling institution = await helper.GetInstellingAsync(CancellationToken.None);

        Assert.Equal("PUBLIC", institution.Afkorting);
        CapturedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://api.somtoday.nl/rest/v1/connect/instelling",
            request.Uri.AbsoluteUri);
        Assert.Null(request.Authorization);
    }

    [Fact]
    public async Task PublicInstitutionLookupPropagatesCancellation()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RecordingHandler handler = new(_ => JsonResponse("{\"instellingen\":[]}"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            OpenAPIHelper.GetPublicInstitutionsAsync(
                new RecordingHttpClientFactory(handler),
                cancellation.Token));
    }

    [Fact]
    public async Task PublicInstitutionLookupFailureIsPropagated()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<ApiException>(() =>
            OpenAPIHelper.GetPublicInstitutionsAsync(
                new RecordingHttpClientFactory(handler),
                CancellationToken.None));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task OnePublicInstitutionRequestCanResolveAllConfiguredSchools()
    {
        Guid secondSchoolUuid = Guid.Parse("22222222-2222-2222-2222-222222222222");
        RecordingHandler handler = new(_ => JsonResponse(
            $$"""
            {"instellingen":[
              {"uuid":"{{SchoolUuid}}","naam":"First","afkorting":"FIRST","brins":[]},
              {"uuid":"{{secondSchoolUuid}}","naam":"Second","afkorting":"SECOND","brins":[]}
            ]}
            """));

        IReadOnlyList<Instelling> institutions = await OpenAPIHelper.GetPublicInstitutionsAsync(
            new RecordingHttpClientFactory(handler),
            CancellationToken.None);

        Assert.Equal("FIRST", OpenAPIHelper.SelectInstitution(institutions, SchoolUuid).Afkorting);
        Assert.Equal("SECOND", OpenAPIHelper.SelectInstitution(institutions, secondSchoolUuid).Afkorting);
        CapturedRequest request = Assert.Single(handler.Requests);
        Assert.Null(request.Authorization);
    }

    [Fact]
    public async Task EmptySourceCollectionsStillReturnSelectedLocationModel()
    {
        RecordingHandler handler = new(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return JsonResponse("{\"access_token\":\"token\"}");
            }

            string path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/lesgroep/", StringComparison.Ordinal))
            {
                return JsonResponse("{\"lesgroepen\":[]}");
            }

            if (path.EndsWith("/medewerker", StringComparison.Ordinal))
            {
                return JsonResponse("{\"medewerkers\":[]}");
            }

            if (path.EndsWith("/leerling", StringComparison.Ordinal))
            {
                return JsonResponse("{\"leerlingen\":[]}");
            }

            if (path.EndsWith("/ouderVerzorger/", StringComparison.Ordinal))
            {
                return JsonResponse("{\"ouderVerzorgers\":[]}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        OpenAPIHelper helper = new(
            "client-id",
            "client-secret",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SomEnvironmentConfig.Prod,
            new RecordingHttpClientFactory(handler),
            new CapturingLogger());
        Vestiging location = new()
        {
            Uuid = Guid.NewGuid(),
            Naam = "Empty location",
            Afkorting = "EMPTY"
        };
        await helper.ConnectAsync(CancellationToken.None);

        VestigingModel model = Assert.Single(await helper.DownloadAllInfoAsync(
            [location],
            enableGuardianSync: true,
            CancellationToken.None));

        Assert.Same(location, model.Vestiging);
        Assert.Empty(model.Lesgroepen);
        Assert.Empty(model.Medewerkers);
        Assert.Empty(model.Leerlingen);
        Assert.Empty(model.OuderVerzorgers);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static SyncConfiguration CreateSyncConfiguration()
    {
        return new SyncConfiguration(
            [SchoolUuid],
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "client-id",
            "client-secret",
            SomEnvironmentConfig.Prod,
            [],
            [],
            false);
    }

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler handler;

        internal RecordingHttpClientFactory(HttpMessageHandler handler)
        {
            this.handler = handler;
        }

        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> responseFactory;

        internal RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            this.responseFactory = responseFactory;
        }

        internal string RequestBody { get; private set; }

        internal List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization?.ToString()));

            if (request.Content is not null)
            {
                RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return responseFactory(request);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        string Authorization);

    private sealed class CapturingLogger : ILogger<OpenAPIHelper>
    {
        internal List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class CapturingProgramLogger : ILogger<Program>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
        }
    }

    private sealed class NullScope : IDisposable
    {
        internal static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

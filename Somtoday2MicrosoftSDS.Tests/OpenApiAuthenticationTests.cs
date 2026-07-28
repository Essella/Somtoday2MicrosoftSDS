using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Somtoday2MicrosoftSDS.Helpers;
using Xunit;

namespace Somtoday2MicrosoftSDS.Tests;

public class OpenApiAuthenticationTests
{
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

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return responseFactory(request);
        }
    }

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

    private sealed class NullScope : IDisposable
    {
        internal static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

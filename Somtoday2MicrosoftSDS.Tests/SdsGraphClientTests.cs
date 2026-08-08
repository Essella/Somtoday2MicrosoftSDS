using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Azure.Core;
using Somtoday2MicrosoftSDS.Helpers;
using Xunit;

namespace Somtoday2MicrosoftSDS.Tests;

public sealed class SdsGraphClientTests
{
    private const string SourceName = "TestSchool";
    private static readonly Guid ConnectorId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData("schoolDataSyncV1", "V1")]
    [InlineData("schoolDataSyncV2Rev1", "V2Rev1")]
    public async Task ResolvesConnectorThroughSourceNameAndSelectsItsFormat(
        string code,
        string expected)
    {
        CaptureHandler graph = new(_ => Json(
            HttpStatusCode.OK,
            $"{{\"value\":[{{\"id\":\"{ConnectorId}\",\"displayName\":\"{SourceName}\",\"@odata.type\":\"#microsoft.graph.industryData.azureDataLakeConnector\",\"fileFormat\":{{\"code\":\"{code}\"}}}}]}}"));
        SdsGraphClient client = Client(graph, new CaptureHandler(_ => throw new InvalidOperationException()));

        SdsConnector connector = await client.GetConnectorAsync(SourceName, CancellationToken.None);

        Assert.Equal(ConnectorId, connector.Id);
        Assert.Equal(Enum.Parse<SdsDatasetFormat>(expected), connector.Format);
        RequestCapture request = Assert.Single(graph.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://graph.microsoft.com/beta/external/industryData/dataConnectors",
            request.Uri.AbsoluteUri);
        Assert.Equal("Bearer", request.Authorization?.Scheme);
        Assert.Equal("graph-token", request.Authorization?.Parameter);
    }

    [Fact]
    public async Task RejectsUnsupportedConnectorFormat()
    {
        CaptureHandler graph = new(_ => Json(
            HttpStatusCode.OK,
            $"{{\"value\":[{{\"id\":\"{ConnectorId}\",\"displayName\":\"{SourceName}\",\"@odata.type\":\"#microsoft.graph.industryData.azureDataLakeConnector\",\"fileFormat\":{{\"code\":\"unsupported\"}}}}]}}"));
        SdsGraphClient client = Client(graph, new CaptureHandler(_ => throw new InvalidOperationException()));

        await Assert.ThrowsAsync<SdsPublicationException>(
            () => client.GetConnectorAsync(SourceName, CancellationToken.None));
    }

    [Theory]
    [InlineData("{\"value\":[]}")]
    [InlineData("{\"value\":[{\"displayName\":\"TestSchool\"},{\"displayName\":\"TestSchool\"}]}")]
    public async Task RejectsMissingOrDuplicateSourceNames(string json)
    {
        SdsGraphClient client = Client(
            new CaptureHandler(_ => Json(HttpStatusCode.OK, json)),
            new CaptureHandler(_ => throw new InvalidOperationException()));

        await Assert.ThrowsAsync<SdsPublicationException>(
            () => client.GetConnectorAsync(SourceName, CancellationToken.None));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"value\":[{\"id\":\"00000000-0000-0000-0000-000000000000\",\"displayName\":\"TestSchool\",\"@odata.type\":\"#microsoft.graph.industryData.azureDataLakeConnector\",\"fileFormat\":{\"code\":\"schoolDataSyncV1\"}}]}")]
    [InlineData("{\"value\":[{\"id\":\"not-a-guid\",\"displayName\":\"TestSchool\",\"@odata.type\":\"#microsoft.graph.industryData.azureDataLakeConnector\",\"fileFormat\":{\"code\":\"schoolDataSyncV1\"}}]}")]
    [InlineData("{\"value\":[{\"id\":\"22222222-2222-2222-2222-222222222222\",\"displayName\":\"TestSchool\",\"@odata.type\":\"#microsoft.graph.industryData.otherConnector\",\"fileFormat\":{\"code\":\"schoolDataSyncV1\"}}]}")]
    public async Task RejectsMalformedConnectorResponses(string json)
    {
        SdsGraphClient client = Client(
            new CaptureHandler(_ => Json(HttpStatusCode.OK, json)),
            new CaptureHandler(_ => throw new InvalidOperationException()));

        await Assert.ThrowsAsync<SdsPublicationException>(
            () => client.GetConnectorAsync(SourceName, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    public async Task GraphRedirectIsAPermanentProtocolFailure(HttpStatusCode status)
    {
        CaptureHandler graph = new(_ => Response(status, "https://redirected.example/graph"));
        SdsGraphClient client = Client(graph, new CaptureHandler(_ => throw new InvalidOperationException()));

        await Assert.ThrowsAsync<SdsPublicationException>(
            () => client.GetConnectorAsync(SourceName, CancellationToken.None));

        Assert.Single(graph.Requests);
    }

    [Fact]
    public async Task UploadsCompleteDatasetToSasWithoutBearerThenValidates()
    {
        int pollCount = 0;
        CaptureHandler graph = new(request => request.RequestUri.AbsolutePath switch
        {
            var path when path.EndsWith("/getUploadSession", StringComparison.Ordinal) => Json(
                HttpStatusCode.OK,
                "{\"sessionUrl\":\"https://temporary.blob.core.windows.net/container/sub?sv=2023-11-03&sig=secret%2Bvalue\"}"),
            var path when path.EndsWith("/validate", StringComparison.Ordinal) => Response(
                HttpStatusCode.Accepted,
                location: "https://graph.microsoft.com/beta/external/industryData/operations/operation-id"),
            var path when path.EndsWith("/operations/operation-id", StringComparison.Ordinal) =>
                Json(HttpStatusCode.OK, ++pollCount == 1 ? "{\"status\":\"running\"}" : "{\"status\":\"succeeded\"}"),
            _ => throw new InvalidOperationException(request.RequestUri.AbsoluteUri)
        });
        CaptureHandler uploads = new(_ => Response(HttpStatusCode.Created));
        SdsGraphClient client = Client(graph, uploads);
        PublicationDataset dataset = new FileHelper().CreateEmptyV1Dataset(includeGuardianSync: false);

        await client.UploadAndValidateAsync(ConnectorId, dataset, CancellationToken.None);

        Assert.Equal(6, uploads.Requests.Count);
        Assert.Contains(
            graph.Requests,
            request => request.Uri.AbsolutePath.EndsWith(
                "/microsoft.graph.industryData.azureDataLakeConnector/getUploadSession",
                StringComparison.Ordinal));
        Assert.All(uploads.Requests, request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Null(request.Authorization);
            Assert.Equal("sv=2023-11-03&sig=secret%2Bvalue", request.Uri.Query.TrimStart('?'));
            Assert.Equal("application/vnd.ms-excel", request.ContentType);
            Assert.Equal(request.Content.LongLength, request.ContentLength);
            Assert.Equal("2023-11-03", request.Header("x-ms-version"));
            Assert.Equal("application/vnd.ms-excel", request.Header("x-ms-blob-content-type"));
            Assert.Equal("BlockBlob", request.Header("x-ms-blob-type"));
            Assert.Equal("PortalUpload", request.Header("x-ms-meta-uploadvia"));
        });
        Assert.Equal(dataset.Files.Select(file => file.Name), uploads.Requests.Select(request => request.Uri.Segments[^1]));
        Assert.Equal(
            dataset.Files.Select(file => file.Content.ToArray()),
            uploads.Requests.Select(request => request.Content),
            ByteArrayComparer.Instance);

        RequestCapture startValidation = Assert.Single(
            graph.Requests,
            request => request.Method == HttpMethod.Post);
        Assert.EndsWith($"/dataConnectors/{ConnectorId:D}/validate", startValidation.Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Null(startValidation.Content);
        Assert.Equal(2, graph.Requests.Count(request => request.Uri.AbsolutePath.Contains("/operations/", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task FailedFileUploadStopsBeforeValidation()
    {
        CaptureHandler graph = new(request => Json(
            HttpStatusCode.OK,
            "{\"sessionUrl\":\"https://temporary.blob.core.windows.net/container?sig=secret\"}"));
        CaptureHandler uploads = new(_ => StorageError(
            HttpStatusCode.BadRequest,
            "InvalidHeaderValue"));
        SdsGraphClient client = Client(graph, uploads);

        SdsPublicationException exception = await Assert.ThrowsAsync<SdsPublicationException>(() => client.UploadAndValidateAsync(
            ConnectorId,
            new FileHelper().CreateEmptyV1Dataset(false),
            CancellationToken.None));

        Assert.Equal("upload SDS CSV file 'School.csv'", exception.SafeOperation);
        Assert.Equal(
            "SdsPublicationException (upload SDS CSV file 'School.csv'; HTTP 400; x-ms-error-code=InvalidHeaderValue)",
            SafeExceptionSummary.Create(exception));
        Assert.Single(uploads.Requests);
        Assert.DoesNotContain(graph.Requests, request => request.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    public async Task SasRedirectStopsBeforeValidation(HttpStatusCode status)
    {
        CaptureHandler graph = new(_ => Json(
            HttpStatusCode.OK,
            "{\"sessionUrl\":\"https://temporary.blob.core.windows.net/container?sig=secret\"}"));
        CaptureHandler uploads = new(_ => Response(status, "https://redirected.example/upload"));
        SdsGraphClient client = Client(graph, uploads);

        await Assert.ThrowsAsync<SdsPublicationException>(() => client.UploadAndValidateAsync(
            ConnectorId,
            new FileHelper().CreateEmptyV1Dataset(false),
            CancellationToken.None));

        Assert.Single(uploads.Requests);
        Assert.DoesNotContain(graph.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task GuardianEnabledV1DatasetRequiresBothGuardianFilesBeforeUpload()
    {
        CaptureHandler graph = new(_ => throw new InvalidOperationException());
        CaptureHandler uploads = new(_ => throw new InvalidOperationException());
        PublicationDataset dataset = new(
            SdsDatasetFormat.V1,
            new FileHelper().CreateEmptyV1Dataset(includeGuardianSync: false).Files,
            IncludesGuardians: true);

        await Assert.ThrowsAsync<SdsPublicationException>(() => Client(graph, uploads).UploadAndValidateAsync(
            ConnectorId,
            dataset,
            CancellationToken.None));

        Assert.Empty(graph.Requests);
        Assert.Empty(uploads.Requests);
    }

    [Fact]
    public async Task GuardianEnabledV2DatasetRequiresRelationshipsFileBeforeUpload()
    {
        CaptureHandler graph = new(_ => throw new InvalidOperationException());
        CaptureHandler uploads = new(_ => throw new InvalidOperationException());
        PublicationDataset dataset = new(
            SdsDatasetFormat.V2Rev1,
            new FileHelper().CreateEmptyV2Dataset(includeGuardianSync: false).Files,
            IncludesGuardians: true);

        await Assert.ThrowsAsync<SdsPublicationException>(() => Client(graph, uploads).UploadAndValidateAsync(
            ConnectorId,
            dataset,
            CancellationToken.None));

        Assert.Empty(graph.Requests);
        Assert.Empty(uploads.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"sessionUrl\":\"http://storage.test/container?sig=secret\"}")]
    [InlineData("{\"sessionUrl\":\"https://storage.test/container\"}")]
    public async Task RejectsMissingOrInvalidSessionUrlBeforeUpload(string json)
    {
        CaptureHandler graph = new(_ => Json(HttpStatusCode.OK, json));
        CaptureHandler uploads = new(_ => Response(HttpStatusCode.Created));
        SdsGraphClient client = Client(graph, uploads);

        await Assert.ThrowsAsync<SdsPublicationException>(() => client.UploadAndValidateAsync(
            ConnectorId,
            new FileHelper().CreateEmptyV1Dataset(false),
            CancellationToken.None));

        Assert.Empty(uploads.Requests);
    }

    [Fact]
    public async Task CancellationIsPreservedBeforeGraphRequest()
    {
        CaptureHandler graph = new(_ => throw new InvalidOperationException());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Client(graph, new CaptureHandler(_ => throw new InvalidOperationException()))
                .GetConnectorAsync(SourceName, cancellation.Token));
        Assert.Empty(graph.Requests);
    }

    [Fact]
    public async Task ValidationDeadlineFailsWithoutTreatingItAsSuccess()
    {
        CaptureHandler graph = new(request => request.RequestUri.AbsolutePath switch
        {
            var path when path.EndsWith("/getUploadSession", StringComparison.Ordinal) => Json(
                HttpStatusCode.OK,
                "{\"sessionUrl\":\"https://storage.test/container?sig=secret\"}"),
            var path when path.EndsWith("/validate", StringComparison.Ordinal) => Response(
                HttpStatusCode.Accepted,
                "https://graph.microsoft.com/beta/external/industryData/operations/operation-id"),
            _ => throw new InvalidOperationException(request.RequestUri.AbsoluteUri)
        });
        CaptureHandler uploads = new(_ => Response(HttpStatusCode.Created));
        Func<TimeSpan, CancellationToken, Task> noDelay = (_, _) => Task.CompletedTask;
        SdsGraphClient client = new(
            new HttpClient(graph),
            new HttpClient(uploads),
            new StaticTokenCredential(),
            new HttpRetryPolicy(delayAsync: noDelay),
            delayAsync: noDelay,
            timeProvider: new ExpiredTimeProvider());

        await Assert.ThrowsAsync<SdsPublicationException>(() => client.UploadAndValidateAsync(
            ConnectorId,
            new FileHelper().CreateEmptyV1Dataset(false),
            CancellationToken.None));
        Assert.DoesNotContain(graph.Requests, request => request.Uri.AbsolutePath.Contains("/operations/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("unknownFutureValue")]
    [InlineData("futureStatus")]
    public async Task FailedAndUnknownValidationStatusesAreNeverSuccess(string status)
    {
        CaptureHandler graph = new(request => request.RequestUri.AbsolutePath switch
        {
            var path when path.EndsWith("/getUploadSession", StringComparison.Ordinal) => Json(
                HttpStatusCode.OK,
                "{\"sessionUrl\":\"https://storage.test/container?sig=secret\"}"),
            var path when path.EndsWith("/validate", StringComparison.Ordinal) => Response(
                HttpStatusCode.Accepted,
                "https://graph.microsoft.com/beta/external/industryData/operations/operation-id"),
            var path when path.Contains("/operations/", StringComparison.Ordinal) => Json(
                HttpStatusCode.OK,
                $"{{\"status\":\"{status}\"}}"),
            _ => throw new InvalidOperationException(request.RequestUri.AbsoluteUri)
        });

        await Assert.ThrowsAsync<SdsPublicationException>(() => Client(
            graph,
            new CaptureHandler(_ => Response(HttpStatusCode.Created)))
            .UploadAndValidateAsync(
                ConnectorId,
                new FileHelper().CreateEmptyV1Dataset(false),
                CancellationToken.None));
    }

    [Fact]
    public void SafeExceptionSummaryNeverIncludesPublicationExceptionMessage()
    {
        string summary = SafeExceptionSummary.Create(
            new SdsPublicationException("sig=private-value", HttpStatusCode.BadRequest));

        Assert.Equal("SdsPublicationException (HTTP 400)", summary);
        Assert.DoesNotContain("private-value", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeExceptionSummaryIncludesTheFixedPublicationOperation()
    {
        string summary = SafeExceptionSummary.Create(
            new SdsPublicationException(
                "response body must remain private",
                HttpStatusCode.BadRequest,
                "create the SDS upload session"));

        Assert.Equal(
            "SdsPublicationException (create the SDS upload session; HTTP 400)",
            summary);
        Assert.DoesNotContain("response body", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoesNotLogAnUnsafeStorageErrorCodeHeader()
    {
        CaptureHandler graph = new(_ => Json(
            HttpStatusCode.OK,
            "{\"sessionUrl\":\"https://temporary.blob.core.windows.net/container?sig=secret\"}"));
        CaptureHandler uploads = new(_ => StorageError(
            HttpStatusCode.BadRequest,
            "private-value?sig=secret"));
        SdsPublicationException exception = await Assert.ThrowsAsync<SdsPublicationException>(() => Client(
            graph,
            uploads).UploadAndValidateAsync(
                ConnectorId,
                new FileHelper().CreateEmptyV1Dataset(false),
                CancellationToken.None));

        Assert.Null(exception.SafeStorageErrorCode);
        Assert.DoesNotContain("private-value", SafeExceptionSummary.Create(exception), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetryPolicyRespectsRetryAfterAndDoesNotReuseRequest()
    {
        int attempts = 0;
        List<TimeSpan> delays = [];
        CaptureHandler handler = new(_ =>
        {
            if (++attempts == 1)
            {
                HttpResponseMessage response = Response(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
                return response;
            }

            return Response(HttpStatusCode.Created);
        });
        HttpRetryPolicy policy = new(delayAsync: (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        });

        using HttpResponseMessage response = await policy.SendAsync(
            new HttpClient(handler),
            () => new HttpRequestMessage(HttpMethod.Put, "https://example.test/file"),
            CancellationToken.None,
            TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(7)], delays);
    }

    [Fact]
    public async Task ValidationPollRetryNeverWaitsLessThanFiveSeconds()
    {
        int pollAttempts = 0;
        List<TimeSpan> delays = [];
        CaptureHandler graph = new(request => request.RequestUri.AbsolutePath switch
        {
            var path when path.EndsWith("/getUploadSession", StringComparison.Ordinal) => Json(
                HttpStatusCode.OK,
                "{\"sessionUrl\":\"https://storage.test/container?sig=secret\"}"),
            var path when path.EndsWith("/validate", StringComparison.Ordinal) => Response(
                HttpStatusCode.Accepted,
                "https://graph.microsoft.com/beta/external/industryData/operations/operation-id"),
            var path when path.Contains("/operations/", StringComparison.Ordinal) => PollResponse(),
            _ => throw new InvalidOperationException(request.RequestUri.AbsoluteUri)
        });
        HttpRetryPolicy retryPolicy = new(delayAsync: (delay, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            delays.Add(delay);
            return Task.CompletedTask;
        });
        SdsGraphClient client = new(
            new HttpClient(graph),
            new HttpClient(new CaptureHandler(_ => Response(HttpStatusCode.Created))),
            new StaticTokenCredential(),
            retryPolicy,
            delayAsync: (delay, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                delays.Add(delay);
                return Task.CompletedTask;
            });

        await client.UploadAndValidateAsync(
            ConnectorId,
            new FileHelper().CreateEmptyV1Dataset(false),
            CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)], delays);
        Assert.Equal(2, pollAttempts);

        HttpResponseMessage PollResponse()
        {
            if (++pollAttempts == 1)
            {
                HttpResponseMessage response = Response(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return response;
            }

            return Json(HttpStatusCode.OK, "{\"status\":\"succeeded\"}");
        }
    }

    [Fact]
    public void UploadUriRequiresHttpsSasAndPreservesItsQuery()
    {
        Uri result = SdsGraphClient.BuildUploadUri(
            new Uri("https://storage.test/container?sv=1&sig=a%2Bb"),
            "School.csv");

        Assert.Equal("https://storage.test/container/School.csv?sv=1&sig=a%2Bb", result.AbsoluteUri);
        Assert.Throws<SdsPublicationException>(() => SdsGraphClient.BuildUploadUri(
            new Uri("http://storage.test/container?sig=x"),
            "School.csv"));
        Assert.Throws<SdsPublicationException>(() => SdsGraphClient.BuildUploadUri(
            new Uri("https://storage.test/container"),
            "School.csv"));
    }

    private static SdsGraphClient Client(CaptureHandler graph, CaptureHandler uploads)
    {
        Func<TimeSpan, CancellationToken, Task> noDelay = (_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };
        return new SdsGraphClient(
            new HttpClient(graph),
            new HttpClient(uploads),
            new StaticTokenCredential(),
            new HttpRetryPolicy(delayAsync: noDelay),
            delayAsync: noDelay);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
    {
        HttpResponseMessage response = Response(status);
        response.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return response;
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string location = null)
    {
        HttpResponseMessage response = new(status);
        if (location is not null)
        {
            response.Headers.Location = new Uri(location);
        }

        return response;
    }

    private static HttpResponseMessage StorageError(HttpStatusCode status, string errorCode)
    {
        HttpResponseMessage response = Response(status);
        response.Headers.Add("x-ms-error-code", errorCode);
        return response;
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("graph-token", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new AccessToken("graph-token", DateTimeOffset.MaxValue));
    }

    private sealed class ExpiredTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            return Interlocked.Exchange(ref _timestamp, TimeSpan.FromMinutes(31).Ticks);
        }
    }

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        internal static readonly ByteArrayComparer Instance = new();

        public bool Equals(byte[] left, byte[] right) => left.AsSpan().SequenceEqual(right);

        public int GetHashCode(byte[] value) => value.Length;
    }

    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal List<RequestCapture> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            byte[] content = request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> contentHeaders = request.Content is null
                ? []
                : request.Content.Headers;
            Dictionary<string, string> headers = request.Headers
                .Concat(contentHeaders)
                .ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);
            Requests.Add(new RequestCapture(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization,
                content,
                request.Content?.Headers.ContentType?.MediaType,
                request.Content?.Headers.ContentLength,
                headers));
            return respond(request);
        }
    }

    private sealed record RequestCapture(
        HttpMethod Method,
        Uri Uri,
        AuthenticationHeaderValue Authorization,
        byte[] Content,
        string ContentType,
        long? ContentLength,
        IReadOnlyDictionary<string, string> Headers)
    {
        internal string Header(string name) => Headers[name];
    }
}

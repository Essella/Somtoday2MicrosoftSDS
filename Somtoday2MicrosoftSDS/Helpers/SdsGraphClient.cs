using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;

namespace Somtoday2MicrosoftSDS.Helpers;

internal sealed record SdsConnector(Guid Id, SdsDatasetFormat Format);

internal sealed class SdsPublicationException : Exception
{
    internal SdsPublicationException(
        string message,
        HttpStatusCode? statusCode = null,
        string safeOperation = null,
        string safeStorageErrorCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        SafeOperation = safeOperation;
        SafeStorageErrorCode = safeStorageErrorCode;
    }

    internal HttpStatusCode? StatusCode { get; }

    // This value is supplied only by the SDS client. It must not contain response data or URIs.
    internal string SafeOperation { get; }

    // This value is a validated Azure Storage error-code header value.
    internal string SafeStorageErrorCode { get; }
}

internal sealed class SdsGraphClient
{
    private const string GraphScope = "https://graph.microsoft.com/.default";
    private static readonly TimeSpan ValidationPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ValidationTimeout = TimeSpan.FromMinutes(30);
    private readonly HttpClient _graphClient;
    private readonly HttpClient _uploadClient;
    private readonly TokenCredential _credential;
    private readonly HttpRetryPolicy _retryPolicy;
    private readonly Uri _graphBaseUri;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly TimeProvider _timeProvider;

    internal SdsGraphClient(
        HttpClient graphClient,
        HttpClient uploadClient,
        TokenCredential credential,
        HttpRetryPolicy retryPolicy = null,
        Uri graphBaseUri = null,
        Func<TimeSpan, CancellationToken, Task> delayAsync = null,
        TimeProvider timeProvider = null)
    {
        _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
        _uploadClient = uploadClient ?? throw new ArgumentNullException(nameof(uploadClient));
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
        _retryPolicy = retryPolicy ?? new HttpRetryPolicy();
        _graphBaseUri = graphBaseUri ?? new Uri("https://graph.microsoft.com/");
        _delayAsync = delayAsync ?? Task.Delay;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal async Task<SdsConnector> GetConnectorAsync(string sourceName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        Uri endpoint = GraphUri("beta/external/industryData/dataConnectors");
        using HttpResponseMessage response = await SendGraphAsync(
            () => new HttpRequestMessage(HttpMethod.Get, endpoint),
            cancellationToken);
        RequireStatus(response, HttpStatusCode.OK, "list SDS data connectors");

        using JsonDocument document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("value", out JsonElement connectors)
            || connectors.ValueKind != JsonValueKind.Array)
        {
            throw new SdsPublicationException("The SDS data connector list did not contain a connector collection");
        }

        JsonElement[] matchingConnectors = connectors
            .EnumerateArray()
            .Where(connector => connector.TryGetProperty("displayName", out JsonElement displayName)
                && displayName.ValueKind == JsonValueKind.String
                && string.Equals(displayName.GetString(), sourceName, StringComparison.Ordinal))
            .ToArray();
        if (matchingConnectors.Length == 0)
        {
            throw new SdsPublicationException("The SDS data connector list did not contain the configured source name");
        }

        if (matchingConnectors.Length > 1)
        {
            throw new SdsPublicationException("The SDS data connector list contains duplicate configured source names");
        }

        JsonElement root = matchingConnectors[0];
        string type = RequiredString(root, "@odata.type");
        if (!string.Equals(
            type,
            "#microsoft.graph.industryData.azureDataLakeConnector",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new SdsPublicationException("The SDS data connector is not an azureDataLakeConnector");
        }

        if (!Guid.TryParse(RequiredString(root, "id"), out Guid connectorId) || connectorId == Guid.Empty)
        {
            throw new SdsPublicationException("The SDS data connector returned an invalid or empty connector ID");
        }

        if (!root.TryGetProperty("fileFormat", out JsonElement fileFormat))
        {
            throw new SdsPublicationException("The SDS data connector did not return a file format");
        }

        SdsDatasetFormat format = RequiredString(fileFormat, "code") switch
        {
            "schoolDataSyncV1" => SdsDatasetFormat.V1,
            "schoolDataSyncV2Rev1" => SdsDatasetFormat.V2Rev1,
            _ => throw new SdsPublicationException("The SDS data connector uses an unsupported file format")
        };

        return new SdsConnector(connectorId, format);
    }

    internal async Task UploadAndValidateAsync(
        Guid connectorId,
        PublicationDataset dataset,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        EnsureCompleteFileSet(dataset);

        Uri sessionUri = await CreateUploadSessionAsync(connectorId, cancellationToken);
        foreach (PublicationFile file in dataset.Files)
        {
            await UploadFileAsync(BuildUploadUri(sessionUri, file.Name), file, cancellationToken);
        }

        Uri validationUri = await StartValidationAsync(connectorId, cancellationToken);
        await PollValidationAsync(validationUri, cancellationToken);
    }

    internal static Uri BuildUploadUri(Uri sessionUri, string fileName)
    {
        ArgumentNullException.ThrowIfNull(sessionUri);
        if (!sessionUri.IsAbsoluteUri
            || !string.Equals(sessionUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(sessionUri.Query)
            || !string.IsNullOrEmpty(sessionUri.Fragment))
        {
            throw new SdsPublicationException("The SDS upload session returned an invalid SAS URL");
        }

        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains('/')
            || fileName.Contains('\\'))
        {
            throw new SdsPublicationException("The SDS dataset contains an invalid file name");
        }

        string absolute = sessionUri.AbsoluteUri;
        int queryIndex = absolute.IndexOf('?', StringComparison.Ordinal);
        string container = absolute[..queryIndex].TrimEnd('/');
        string query = absolute[queryIndex..];
        return new Uri($"{container}/{Uri.EscapeDataString(fileName)}{query}", UriKind.Absolute);
    }

    internal static Uri BuildDataLakeOperationUri(Uri fileUri, string operationQuery)
    {
        ArgumentNullException.ThrowIfNull(fileUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationQuery);
        if (!fileUri.IsAbsoluteUri
            || !string.Equals(fileUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(fileUri.Query)
            || !string.IsNullOrEmpty(fileUri.Fragment))
        {
            throw new SdsPublicationException("The SDS upload session returned an invalid SAS URL");
        }

        return new Uri($"{fileUri.AbsoluteUri}&{operationQuery}", UriKind.Absolute);
    }

    private async Task<Uri> CreateUploadSessionAsync(Guid connectorId, CancellationToken cancellationToken)
    {
        Uri endpoint = GraphUri(
            $"beta/external/industryData/dataConnectors/{connectorId:D}/microsoft.graph.industryData.azureDataLakeConnector/getUploadSession?resetSession=true");
        using HttpResponseMessage response = await SendGraphAsync(
            () => new HttpRequestMessage(HttpMethod.Get, endpoint),
            cancellationToken);
        RequireStatus(response, HttpStatusCode.OK, "create the SDS upload session");

        using JsonDocument document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        string rawSessionUrl = RequiredString(document.RootElement, "sessionUrl");
        if (!Uri.TryCreate(rawSessionUrl, UriKind.Absolute, out Uri sessionUri))
        {
            throw new SdsPublicationException("The SDS upload session returned an invalid SAS URL");
        }

        _ = BuildUploadUri(sessionUri, "probe.csv");
        return sessionUri;
    }

    private async Task UploadFileAsync(
        Uri fileUri,
        PublicationFile file,
        CancellationToken cancellationToken)
    {
        byte[] bytes = file.Content.ToArray();
        using HttpResponseMessage createResponse = await _retryPolicy.SendAsync(
            _uploadClient,
            () =>
            {
                HttpRequestMessage request = new(
                    HttpMethod.Put,
                    BuildDataLakeOperationUri(fileUri, "resource=file"));
                ByteArrayContent content = new([]);
                content.Headers.ContentLength = 0;
                request.Content = content;
                request.Headers.TryAddWithoutValidation("x-ms-version", "2023-11-03");
                return request;
            },
            cancellationToken);
        RequireStatus(createResponse, HttpStatusCode.Created, $"create SDS file '{file.Name}'");

        using HttpResponseMessage appendResponse = await _retryPolicy.SendAsync(
            _uploadClient,
            () =>
            {
                HttpRequestMessage request = new(
                    HttpMethod.Patch,
                    BuildDataLakeOperationUri(fileUri, "action=append&position=0"));
                ByteArrayContent content = new(bytes);
                content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
                content.Headers.ContentLength = bytes.LongLength;
                request.Content = content;
                request.Headers.TryAddWithoutValidation("x-ms-version", "2023-11-03");
                return request;
            },
            cancellationToken);
        RequireStatus(appendResponse, HttpStatusCode.Accepted, $"append SDS CSV file '{file.Name}'");

        using HttpResponseMessage flushResponse = await _retryPolicy.SendAsync(
            _uploadClient,
            () =>
            {
                HttpRequestMessage request = new(
                    HttpMethod.Patch,
                    BuildDataLakeOperationUri(fileUri, $"action=flush&position={bytes.LongLength}"));
                ByteArrayContent content = new([]);
                content.Headers.ContentLength = 0;
                request.Content = content;
                request.Headers.TryAddWithoutValidation("x-ms-version", "2023-11-03");
                request.Headers.TryAddWithoutValidation("x-ms-content-type", "application/vnd.ms-excel");
                return request;
            },
            cancellationToken);
        RequireStatus(flushResponse, HttpStatusCode.OK, $"flush SDS CSV file '{file.Name}'");
    }

    private async Task<Uri> StartValidationAsync(Guid connectorId, CancellationToken cancellationToken)
    {
        Uri endpoint = GraphUri($"beta/external/industryData/dataConnectors/{connectorId:D}/validate");
        using HttpResponseMessage response = await SendGraphAsync(
            () => new HttpRequestMessage(HttpMethod.Post, endpoint),
            cancellationToken);
        RequireStatus(response, HttpStatusCode.Accepted, "start SDS validation");

        Uri location = response.Headers.Location;
        if (location is null)
        {
            throw new SdsPublicationException(
                "SDS validation did not return a polling location",
                safeOperation: "read SDS validation polling location: missing");
        }

        Uri pollingLocation = location.IsAbsoluteUri
            ? location
            : new Uri(_graphBaseUri, location);
        if (!string.Equals(pollingLocation.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(pollingLocation.Host, "graph.microsoft.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new SdsPublicationException(
                "SDS validation returned an untrusted polling location",
                safeOperation: "read SDS validation polling location: untrusted location");
        }

        return pollingLocation;
    }

    private async Task PollValidationAsync(Uri location, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = new(ValidationTimeout, _timeProvider);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        CancellationToken pollCancellationToken = linked.Token;
        long started = _timeProvider.GetTimestamp();
        try
        {
            while (_timeProvider.GetElapsedTime(started) < ValidationTimeout)
            {
                await _delayAsync(ValidationPollInterval, pollCancellationToken);
                using HttpResponseMessage response = await SendGraphAsync(
                    () => new HttpRequestMessage(HttpMethod.Get, location),
                    pollCancellationToken,
                    ValidationPollInterval);
                RequireStatus(response, HttpStatusCode.OK, "poll SDS validation");

                using JsonDocument document = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(pollCancellationToken),
                    cancellationToken: pollCancellationToken);
                string status = RequiredString(document.RootElement, "status");
                switch (status.ToLowerInvariant())
                {
                    case "notstarted":
                    case "running":
                        continue;
                    case "succeeded":
                        return;
                    case "failed":
                        throw new SdsPublicationException("SDS validation failed");
                    default:
                        throw new SdsPublicationException("SDS validation returned an unknown status");
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new SdsPublicationException("SDS validation did not complete within 30 minutes");
        }

        throw new SdsPublicationException("SDS validation did not complete within 30 minutes");
    }

    private async Task<HttpResponseMessage> SendGraphAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken,
        TimeSpan? minimumRetryDelay = null)
    {
        AccessToken token = await _credential.GetTokenAsync(
            new TokenRequestContext([GraphScope]),
            cancellationToken);
        return await _retryPolicy.SendAsync(
            _graphClient,
            () =>
            {
                HttpRequestMessage request = requestFactory();
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                return request;
            },
            cancellationToken,
            minimumRetryDelay);
    }

    private Uri GraphUri(string relativePath)
    {
        return new Uri(_graphBaseUri, relativePath);
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new SdsPublicationException($"The SDS response did not contain required field '{propertyName}'");
        }

        return property.GetString();
    }

    private static void RequireStatus(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string operation)
    {
        if (response.StatusCode != expected)
        {
            throw new SdsPublicationException(
                $"Unable to {operation}",
                response.StatusCode,
                operation,
                GetSafeStorageErrorCode(response));
        }
    }

    private static string GetSafeStorageErrorCode(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("x-ms-error-code", out IEnumerable<string> values))
        {
            return null;
        }

        string value = values.SingleOrDefault();
        return value is { Length: > 0 and <= 128 }
            && value.All(character => char.IsAsciiLetterOrDigit(character))
                ? value
                : null;
    }

    private static void EnsureCompleteFileSet(PublicationDataset dataset)
    {
        string[] coreNames = dataset.Format == SdsDatasetFormat.V1
            ? ["School.csv", "Section.csv", "Teacher.csv", "Student.csv", "TeacherRoster.csv", "StudentEnrollment.csv"]
            : ["orgs.csv", "users.csv", "roles.csv", "classes.csv", "enrollments.csv"];
        string[] guardianNames = dataset.IncludesGuardians
            ? dataset.Format == SdsDatasetFormat.V1
                ? ["User.csv", "Guardianrelationship.csv"]
                : ["relationships.csv"]
            : [];

        HashSet<string> names = dataset.Files.Select(file => file.Name).ToHashSet(StringComparer.Ordinal);
        HashSet<string> expectedNames = coreNames
            .Concat(guardianNames)
            .ToHashSet(StringComparer.Ordinal);
        if (names.Count != dataset.Files.Count || !names.SetEquals(expectedNames))
        {
            throw new SdsPublicationException("The SDS dataset does not contain one complete file set");
        }
    }
}

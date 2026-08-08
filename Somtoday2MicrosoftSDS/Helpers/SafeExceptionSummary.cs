using Azure;

namespace Somtoday2MicrosoftSDS.Helpers;

internal static class SafeExceptionSummary
{
    internal static string Create(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            RequestFailedException requestFailed =>
                $"{requestFailed.GetType().Name} (HTTP {requestFailed.Status})",
            ApiException apiException =>
                $"{apiException.GetType().Name} (HTTP {apiException.StatusCode})",
            HttpRequestException httpRequest when httpRequest.StatusCode.HasValue =>
                $"{httpRequest.GetType().Name} (HTTP {(int)httpRequest.StatusCode.Value})",
            SdsPublicationException publication when publication.StatusCode.HasValue
                && !string.IsNullOrWhiteSpace(publication.SafeOperation) => FormatPublicationFailure(publication),
            SdsPublicationException publication when publication.StatusCode.HasValue =>
                $"{publication.GetType().Name} (HTTP {(int)publication.StatusCode.Value})",
            SdsPublicationException publication when !string.IsNullOrWhiteSpace(publication.SafeOperation) =>
                $"{publication.GetType().Name} ({publication.SafeOperation})",
            _ => exception.GetType().Name
        };
    }

    private static string FormatPublicationFailure(SdsPublicationException publication)
    {
        string summary = $"{publication.GetType().Name} ({publication.SafeOperation}; HTTP {(int)publication.StatusCode.Value}";
        return !string.IsNullOrWhiteSpace(publication.SafeStorageErrorCode)
            ? $"{summary}; x-ms-error-code={publication.SafeStorageErrorCode})"
            : $"{summary})";
    }
}

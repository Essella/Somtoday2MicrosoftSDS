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
            _ => exception.GetType().Name
        };
    }
}

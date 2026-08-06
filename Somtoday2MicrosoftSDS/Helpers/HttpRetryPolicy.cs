using System.Net;

namespace Somtoday2MicrosoftSDS.Helpers;

internal sealed class HttpRetryPolicy
{
    private readonly int _totalAttempts;
    private readonly TimeSpan _defaultDelay;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    internal HttpRetryPolicy(
        int totalAttempts = 4,
        TimeSpan? defaultDelay = null,
        Func<TimeSpan, CancellationToken, Task> delayAsync = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(totalAttempts, 1);
        _totalAttempts = totalAttempts;
        _defaultDelay = defaultDelay ?? TimeSpan.FromSeconds(2);
        _delayAsync = delayAsync ?? Task.Delay;
    }

    internal async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using HttpRequestMessage request = requestFactory();
                HttpResponseMessage response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (attempt == _totalAttempts || !IsTransient(response.StatusCode))
                {
                    return response;
                }

                TimeSpan delay = GetRetryDelay(response);
                response.Dispose();
                await _delayAsync(delay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _totalAttempts && IsTransient(ex))
            {
                await _delayAsync(_defaultDelay, cancellationToken);
            }
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        int status = (int)statusCode;
        return status is 408 or 429 || status >= 500;
    }

    private static bool IsTransient(Exception exception)
    {
        return exception is HttpRequestException
            or TaskCanceledException;
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta >= TimeSpan.Zero)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is DateTimeOffset retryAt)
        {
            TimeSpan delay = retryAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                return delay;
            }
        }

        return _defaultDelay;
    }
}

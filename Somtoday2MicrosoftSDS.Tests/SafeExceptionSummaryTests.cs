using Somtoday2MicrosoftSDS.Helpers;
using Xunit;

namespace Somtoday2MicrosoftSDS.Tests;

public class SafeExceptionSummaryTests
{
    [Fact]
    public void GenericExceptionDoesNotExposeItsMessage()
    {
        const string secret = "super-secret-value";

        string summary = SafeExceptionSummary.Create(new InvalidOperationException(secret));

        Assert.Equal(nameof(InvalidOperationException), summary);
        Assert.DoesNotContain(secret, summary, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedApiExceptionExposesStatusButNotResponseBody()
    {
        const string secret = "token-or-personal-data";
        ApiException exception = new(
            "Request failed",
            403,
            $"response contains {secret}",
            new Dictionary<string, IEnumerable<string>>(),
            null);

        string summary = SafeExceptionSummary.Create(exception);

        Assert.Contains("HTTP 403", summary, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, summary, StringComparison.Ordinal);
        Assert.DoesNotContain("response contains", summary, StringComparison.Ordinal);
    }
}

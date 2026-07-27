using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using TaskTracker.Core.GitHub;

namespace TaskTracker.Core.Tests;

public class GitHubRateLimitTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    private static HttpResponseMessage Response(HttpStatusCode status, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent("{}") };
        foreach (var (name, value) in headers)
            response.Headers.TryAddWithoutValidation(name, value);
        return response;
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public void OnlyRateLimitStatusesAreRetried(HttpStatusCode status)
    {
        using var response = Response(status, ("Retry-After", "5"));
        Assert.Null(GitHubRateLimit.RetryDelay(response, Now));
    }

    [Fact]
    public void RetryAfterSecondsIsHonoured()
    {
        using var response = Response(HttpStatusCode.TooManyRequests, ("Retry-After", "7"));
        Assert.Equal(TimeSpan.FromSeconds(7), GitHubRateLimit.RetryDelay(response, Now));
    }

    [Fact]
    public void RetryAfterOnA403IsHonouredToo()
    {
        // A secondary rate limit arrives as 403, not 429 — the whole reason this exists.
        using var response = Response(HttpStatusCode.Forbidden, ("Retry-After", "3"));
        Assert.Equal(TimeSpan.FromSeconds(3), GitHubRateLimit.RetryDelay(response, Now));
    }

    [Fact]
    public void RetryAfterAsAnHttpDateIsMeasuredFromNow()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(Now.AddSeconds(20));
        Assert.Equal(TimeSpan.FromSeconds(20), GitHubRateLimit.RetryDelay(response, Now));
    }

    [Fact]
    public void ARetryAfterDateAlreadyPastMeansRetryImmediately()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(Now.AddSeconds(-30));
        Assert.Equal(TimeSpan.Zero, GitHubRateLimit.RetryDelay(response, Now));
    }

    [Fact]
    public void AWaitLongerThanTheCapIsNotWorthSittingThrough()
    {
        using var response = Response(HttpStatusCode.TooManyRequests,
            ("Retry-After", ((int)GitHubRateLimit.MaxWait.TotalSeconds + 1).ToString()));
        Assert.Null(GitHubRateLimit.RetryDelay(response, Now));
    }

    [Fact]
    public void AWaitExactlyAtTheCapIsStillAccepted()
    {
        using var response = Response(HttpStatusCode.TooManyRequests,
            ("Retry-After", ((int)GitHubRateLimit.MaxWait.TotalSeconds).ToString()));
        Assert.Equal(GitHubRateLimit.MaxWait, GitHubRateLimit.RetryDelay(response, Now));
    }

    [Fact]
    public void A403WithQuotaRemainingIsAPermissionProblem()
    {
        // Retrying a bad token would only delay the message the user needs to see.
        using var response = Response(HttpStatusCode.Forbidden,
            ("x-ratelimit-remaining", "4999"), ("x-ratelimit-reset", ToUnix(Now.AddSeconds(10))));
        Assert.Null(GitHubRateLimit.RetryDelay(response, Now));
    }

    [Fact]
    public void A403WithNoRateLimitHeadersAtAllIsNotRetried()
    {
        using var response = Response(HttpStatusCode.Forbidden);
        Assert.Null(GitHubRateLimit.RetryDelay(response, Now));
    }

    [Fact]
    public void ASpentQuotaWaitsUntilTheResetTime()
    {
        using var response = Response(HttpStatusCode.Forbidden,
            ("x-ratelimit-remaining", "0"), ("x-ratelimit-reset", ToUnix(Now.AddSeconds(30))));
        Assert.Equal(TimeSpan.FromSeconds(30), GitHubRateLimit.RetryDelay(response, Now));
    }

    [Fact]
    public void AResetTooFarOutIsNotWaitedFor()
    {
        // The primary hourly quota resets in minutes, not seconds.
        using var response = Response(HttpStatusCode.Forbidden,
            ("x-ratelimit-remaining", "0"), ("x-ratelimit-reset", ToUnix(Now.AddMinutes(45))));
        Assert.Null(GitHubRateLimit.RetryDelay(response, Now));
    }

    [Fact]
    public void ASpentQuotaWithNoResetTimeFallsBackToAShortWait()
    {
        using var response = Response(HttpStatusCode.TooManyRequests, ("x-ratelimit-remaining", "0"));
        Assert.Equal(TimeSpan.FromSeconds(1), GitHubRateLimit.RetryDelay(response, Now));
    }

    [Fact]
    public void UnparseableHeaderValuesAreIgnoredRatherThanCrashing()
    {
        using var response = Response(HttpStatusCode.Forbidden,
            ("x-ratelimit-remaining", "not-a-number"));
        Assert.Null(GitHubRateLimit.RetryDelay(response, Now));
    }

    private static string ToUnix(DateTimeOffset when) => when.ToUnixTimeSeconds().ToString();
}

using System.Net;
using System.Net.Http;

namespace TaskTracker.Core.GitHub
{
    /// <summary>
    /// Reads GitHub's rate-limit signals off a response and decides how long to wait
    /// before trying again.
    ///
    /// GitHub answers a secondary rate limit with 403 — the same status it uses for a bad
    /// token — so the status alone cannot tell "slow down" from "you may not do this".
    /// The rate-limit headers are what separate them, and without checking them a sync
    /// during a burst failed outright with a generic message.
    /// </summary>
    public static class GitHubRateLimit
    {
        /// <summary>
        /// Longest wait worth sitting through. Beyond this the primary hourly quota is
        /// usually exhausted, and blocking a sync for tens of minutes helps nobody —
        /// better to report the failure and let the user retry later.
        /// </summary>
        public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(60);

        /// <summary>Total attempts for one request, so at most two retries.</summary>
        public const int MaxAttempts = 3;

        /// <summary>
        /// How long to wait before retrying, or null when this response should not be
        /// retried at all — either it is not a rate limit, or the wait is too long to be
        /// worth it.
        /// </summary>
        /// <param name="now">Current time, for the HTTP-date form of Retry-After.</param>
        public static TimeSpan? RetryDelay(HttpResponseMessage response, DateTimeOffset now)
        {
            var status = (int)response.StatusCode;
            if (status != (int)HttpStatusCode.TooManyRequests && status != (int)HttpStatusCode.Forbidden)
                return null;

            // Retry-After is authoritative when present, in either of its two forms.
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter?.Delta is { } delta)
                return Clamp(delta);
            if (retryAfter?.Date is { } date)
                return Clamp(date - now);

            // Otherwise only a spent quota justifies waiting; a 403 without one is a
            // permission problem, and retrying it just delays the real error.
            if (ReadLong(response, "x-ratelimit-remaining") is not 0)
                return null;

            if (ReadLong(response, "x-ratelimit-reset") is { } reset)
                return Clamp(DateTimeOffset.FromUnixTimeSeconds(reset) - now);

            // Quota is spent but the reset time is missing. A short wait is still better
            // than failing immediately, and MaxAttempts keeps it bounded.
            return TimeSpan.FromSeconds(1);
        }

        private static TimeSpan? Clamp(TimeSpan wait)
        {
            if (wait > MaxWait)
                return null;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        private static long? ReadLong(HttpResponseMessage response, string header)
        {
            if (!response.Headers.TryGetValues(header, out var values))
                return null;
            return long.TryParse(values.FirstOrDefault(), out var parsed) ? parsed : null;
        }
    }
}

using System.Net;

namespace Codelab.Integration.Resilience;

/// <summary>
/// Reads the upstream's Retry-After header on a 429 or 503. Null means "use the exponential back-off".
/// </summary>
internal static class RetryAfterDelay
{
    private static readonly HashSet<HttpStatusCode> StatusesWithRetryAfter =
        [HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable];

    /// <summary>The upstream's requested delay, capped at <paramref name="maxDelay"/>; null when absent or in the past.</summary>
    public static TimeSpan? From(HttpResponseMessage? response, TimeSpan maxDelay, TimeProvider timeProvider)
    {
        var header = response?.Headers.RetryAfter;
        if (header is null || !StatusesWithRetryAfter.Contains(response!.StatusCode))
        {
            return null;
        }

        var delay = header.Delta ?? header.Date - timeProvider.GetUtcNow();
        if (delay is null || delay <= TimeSpan.Zero)
        {
            return null;
        }

        return delay > maxDelay ? maxDelay : delay;
    }
}

namespace Codelab.Integration.Outbound;

/// <summary>
/// The default and limit values of an outbound client. Every number the engine uses lives here, so a reader finds
/// the whole policy in one place and a host overrides it through <see cref="OutboundClientOptions"/>.
/// </summary>
public static class OutboundDefaults
{
    /// <summary>Retries after the first attempt (so up to 3 attempts in total). 0 turns retries off.</summary>
    public const int MaxRetries = 2;

    /// <summary>The highest retry count the validator accepts; more only multiplies load on a failing upstream.</summary>
    public const int MaxRetriesCeiling = 10;

    /// <summary>The first back-off delay; later delays grow exponentially, with jitter, up to <see cref="RetryMaxDelay"/>.</summary>
    public static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>The longest single back-off delay, also the cap on an upstream Retry-After value.</summary>
    public static readonly TimeSpan RetryMaxDelay = TimeSpan.FromSeconds(10);

    /// <summary>The timeout of one attempt.</summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The timeout of the whole call, every attempt and back-off delay included.</summary>
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The shortest timeout Polly accepts.</summary>
    public static readonly TimeSpan MinimumTimeout = TimeSpan.FromMilliseconds(10);
}

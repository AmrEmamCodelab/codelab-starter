using Codelab.Integration.Certificates;

namespace Codelab.Integration.Outbound;

/// <summary>
/// The options of one named outbound client: where it calls, how it retries and times out, and how it trusts the
/// server certificate. Bound per client name, validated at start-up (<see cref="OutboundClientOptionsValidator"/>).
/// </summary>
public sealed class OutboundClientOptions
{
    /// <summary>The absolute base address of the upstream; requests use paths relative to it.</summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// Allows an <c>http</c> base address. Off by default: certificate validation protects only an <c>https</c> call,
    /// so a plain-text upstream must be a deliberate, reviewed choice.
    /// </summary>
    public bool AllowPlainHttp { get; set; }

    /// <summary>The retry policy.</summary>
    public OutboundRetryOptions Retry { get; set; } = new();

    /// <summary>The attempt and total timeouts.</summary>
    public OutboundTimeoutOptions Timeout { get; set; } = new();

    /// <summary>How the server certificate is trusted. Validation is always on; only the trust anchor varies.</summary>
    public OutboundCertificateOptions Certificate { get; set; } = new();
}

/// <summary>
/// The retry policy. Only retry-safe requests are retried (GET, HEAD, OPTIONS, or a request marked with
/// <see cref="OutboundRequestExtensions.MarkIdempotent"/>), and only after a transient failure.
/// </summary>
public sealed class OutboundRetryOptions
{
    /// <summary>Retries after the first attempt; 0 turns retries off. At most <see cref="OutboundDefaults.MaxRetriesCeiling"/>.</summary>
    public int MaxRetries { get; set; } = OutboundDefaults.MaxRetries;

    /// <summary>The first back-off delay (exponential growth with jitter after it).</summary>
    public TimeSpan BaseDelay { get; set; } = OutboundDefaults.RetryBaseDelay;

    /// <summary>The longest back-off delay; also caps an upstream Retry-After value.</summary>
    public TimeSpan MaxDelay { get; set; } = OutboundDefaults.RetryMaxDelay;

    /// <summary>Adds jitter to the back-off so many clients do not retry in lockstep.</summary>
    public bool UseJitter { get; set; } = true;
}

/// <summary>The attempt and total timeouts of an outbound call.</summary>
public sealed class OutboundTimeoutOptions
{
    /// <summary>The timeout of one attempt.</summary>
    public TimeSpan AttemptTimeout { get; set; } = OutboundDefaults.AttemptTimeout;

    /// <summary>The timeout of the whole call, every attempt and back-off delay included.</summary>
    public TimeSpan TotalTimeout { get; set; } = OutboundDefaults.TotalTimeout;
}

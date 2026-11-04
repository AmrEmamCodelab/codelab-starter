namespace Codelab.Integration.Classification;

/// <summary>
/// What went wrong with an outbound call. Stored and logged as its integer value, so the numbers never change.
/// </summary>
public enum OutboundFailureKind
{
    /// <summary>A failure the classifier does not recognise. Not retried.</summary>
    Unknown = 0,

    /// <summary>An attempt or the whole call timed out, or the upstream answered 408. Transient.</summary>
    Timeout = 1,

    /// <summary>The connection could not be opened or broke (DNS failure or refused or reset). Transient.</summary>
    ConnectionFailed = 2,

    /// <summary>The TLS handshake failed, typically because the server certificate was refused. Never retried.</summary>
    CertificateRejected = 3,

    /// <summary>The upstream answered 429. Transient; a Retry-After value is honoured up to the maximum delay.</summary>
    Throttled = 4,

    /// <summary>The upstream answered 401 or 403. Not retried: the credentials or the grant must change first.</summary>
    Unauthorized = 5,

    /// <summary>Any other 4xx answer: the request itself is wrong. Not retried.</summary>
    ClientError = 6,

    /// <summary>A 5xx answer. Transient for 500 502 503 and 504; not for the others such as 501 and 505.</summary>
    ServerError = 7,

    /// <summary>The caller cancelled the call. Never retried.</summary>
    Cancelled = 8,
}

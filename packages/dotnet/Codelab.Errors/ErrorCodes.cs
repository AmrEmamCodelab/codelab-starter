namespace Codelab.Errors;

/// <summary>
/// The platform's machine-readable failure vocabulary. Every error a Codelab host emits (thrown, framework-generated,
/// validation or refusal) carries exactly one code in the envelope's <c>code</c> member, and the client branches on
/// the code, not on the status or the text. The SPA is bilingual, so the server never authors the sentence a user
/// reads: <c>errors.&lt;CODE&gt;</c> in the <c>ar</c> and <c>en</c> bundles does. A module adds its own codes in its
/// own <c>ErrorCodes</c> class.
/// </summary>
public static class ErrorCodes
{
    /// <summary>Well-formed request, invalid field values. Carries a per-field <c>errors</c> dictionary.</summary>
    public const string Validation = "VALIDATION_FAILED";

    /// <summary>The request could not be parsed at all: bad JSON, wrong content type, wrong method.</summary>
    public const string Malformed = "MALFORMED_REQUEST";

    /// <summary>Well-formed and permitted, but a business rule says no.</summary>
    public const string BusinessRule = "BUSINESS_RULE_VIOLATED";

    /// <summary>No authenticated caller.</summary>
    public const string Unauthenticated = "UNAUTHENTICATED";

    /// <summary>The caller may not do this.</summary>
    public const string Forbidden = "FORBIDDEN";

    /// <summary>The resource does not exist (or belongs to someone the caller may not see).</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>The request conflicts with the resource's current state.</summary>
    public const string Conflict = "CONFLICT";

    /// <summary>Someone else changed the record first; reload and retry.</summary>
    public const string Concurrency = "CONCURRENCY";

    /// <summary>The request body is larger than the server accepts.</summary>
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";

    /// <summary>Transient contention; the client should retry after the <c>Retry-After</c> header.</summary>
    public const string Retryable = "RETRYABLE";

    /// <summary>A dependency is down: neither the server's fault nor the caller's.</summary>
    public const string Upstream = "UPSTREAM_UNAVAILABLE";

    /// <summary>The default. Says nothing about the cause on purpose.</summary>
    public const string InternalError = "INTERNAL_ERROR";
}

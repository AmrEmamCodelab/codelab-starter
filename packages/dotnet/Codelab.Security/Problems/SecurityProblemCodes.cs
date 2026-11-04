using Codelab.Security.Signing;

namespace Codelab.Security.Problems;

/// <summary>
/// The stable <c>code</c> values security controls put on their problem+json refusals. The client maps them (a
/// code-less 401 reads as session expiry and logs the user out, so every 401 here carries a code).
/// </summary>
public static class SecurityProblemCodes
{
    /// <summary>No request proof on an authenticated, non-exempt request.</summary>
    public const string SignatureMissing = "signature_missing";

    /// <summary>The proof is malformed or its MAC does not verify.</summary>
    public const string SignatureInvalid = "signature_invalid";

    /// <summary>The proof timestamp is outside the window.</summary>
    public const string SignatureStale = "signature_stale";

    /// <summary>The token yields no signing key.</summary>
    public const string SignatureKeyUnbound = "signature_key_unbound";

    /// <summary>The proof nonce was already used.</summary>
    public const string SignatureReplayed = "signature_replayed";

    /// <summary>The request body is larger than the endpoint accepts.</summary>
    public const string PayloadTooLarge = "payload_too_large";

    /// <summary>The session was revoked (logout, admin revoke).</summary>
    public const string SessionRevoked = "session_revoked";

    /// <summary>The user's session version moved on (password change, deactivation, grant change).</summary>
    public const string SessionVersionStale = "session_version_stale";

    /// <summary>The token carries no session id although revocation requires one.</summary>
    public const string SessionUnidentified = "session_unidentified";

    /// <summary>Too many requests from this client.</summary>
    public const string RateLimited = "rate_limited";

    /// <summary>The problem code for a proof rejection reason.</summary>
    public static string ForRejection(ProofRejectionReason reason) => reason switch
    {
        ProofRejectionReason.Missing => SignatureMissing,
        ProofRejectionReason.Stale => SignatureStale,
        ProofRejectionReason.KeyUnbound => SignatureKeyUnbound,
        ProofRejectionReason.Replayed => SignatureReplayed,
        _ => SignatureInvalid,
    };
}

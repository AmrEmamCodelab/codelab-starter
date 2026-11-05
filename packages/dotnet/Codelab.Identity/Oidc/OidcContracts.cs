namespace Codelab.Identity.Oidc;

/// <summary>
/// The OpenID Connect authorization code flow with PKCE, as three steps the host's endpoints call: build the
/// authorization URL (the host keeps the pending values in a sealed cookie), complete the callback server-side, and
/// build the RP-initiated logout URL.
/// </summary>
public interface IOidcIdentityProvider
{
    /// <summary>
    /// A new authorization request: the URL to redirect the browser to, and the values the callback must present
    /// (fresh state, nonce and PKCE verifier; S256 challenge in the URL). <paramref name="returnUrl"/> is kept only
    /// when it is a root-relative path; otherwise <see cref="OidcAuthorizationRequest.ReturnUrl"/> is <c>/</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The provider is not enabled.</exception>
    OidcAuthorizationRequest BeginAuthorization(string returnUrl);

    /// <summary>
    /// Completes a callback: checks the state in constant time, redeems the code over the back channel with the
    /// verifier, and validates the id_token (signature, algorithm, issuer, audience, lifetime, nonce). Never throws for a
    /// provider fault; cancellation propagates.
    /// </summary>
    Task<OidcSignInResult> CompleteAsync(OidcCallback callback, OidcPendingAuthorization pending, CancellationToken cancellationToken);

    /// <summary>
    /// The end_session URL with <c>id_token_hint</c>, <c>client_id</c> and <c>post_logout_redirect_uri</c>
    /// (<paramref name="postLogoutRedirectUri"/>, else the configured one when set), for a top-level browser GET.
    /// </summary>
    /// <exception cref="InvalidOperationException">The provider is not enabled.</exception>
    Uri BuildEndSessionUrl(string idTokenHint, Uri? postLogoutRedirectUri);
}

/// <summary>A started sign-in. Keep <see cref="State"/>, <see cref="CodeVerifier"/> and <see cref="Nonce"/> server-sealed until the callback.</summary>
public sealed record OidcAuthorizationRequest(Uri AuthorizationUrl, string State, string CodeVerifier, string Nonce, string ReturnUrl)
{
    /// <summary>The URL and return path only; the secrets of the sign-in are not printed.</summary>
    public override string ToString() =>
        $"{nameof(OidcAuthorizationRequest)} {{ {nameof(AuthorizationUrl)}: {AuthorizationUrl}, {nameof(ReturnUrl)}: {ReturnUrl} }}";
}

/// <summary>What the provider sent back to the redirect URI.</summary>
public sealed record OidcCallback(string Code, string State)
{
    /// <summary>Nothing printed: both values are single-use secrets.</summary>
    public override string ToString() => nameof(OidcCallback);
}

/// <summary>The values <see cref="IOidcIdentityProvider.BeginAuthorization"/> issued, as the host kept them.</summary>
public sealed record OidcPendingAuthorization(string State, string CodeVerifier, string Nonce)
{
    /// <summary>Nothing printed: every value is a secret of the sign-in.</summary>
    public override string ToString() => nameof(OidcPendingAuthorization);
}

/// <summary>The outcome of a callback. Stored and logged as an integer.</summary>
public enum OidcSignInStatus
{
    /// <summary>The code was redeemed and the id_token is valid.</summary>
    Succeeded = 1,

    /// <summary>Something presented was wrong: state; code; token response or id_token.</summary>
    Rejected = 2,

    /// <summary>The provider could not be asked (unreachable, timed out, 5xx, signing keys unobtainable).</summary>
    Unavailable = 3,
}

/// <summary>Why a callback did not succeed. Stored and logged as an integer.</summary>
public enum OidcFailureReason
{
    /// <summary>The callback's state is not the pending sign-in's.</summary>
    StateMismatch = 1,

    /// <summary>The callback carried no code.</summary>
    MissingCode = 2,

    /// <summary>The token endpoint refused the code (4xx).</summary>
    TokenRequestRejected = 3,

    /// <summary>The token endpoint answered without a usable id_token.</summary>
    TokenResponseInvalid = 4,

    /// <summary>The id_token failed validation.</summary>
    IdTokenInvalid = 5,

    /// <summary>The provider or its signing keys could not be reached.</summary>
    ProviderUnavailable = 6,
}

/// <summary>
/// The signed-in identity, from the validated id_token. Absent or non-string claims are null.
/// <see cref="Email"/> is null unless verified while <see cref="OidcOptions.RequireVerifiedEmail"/> is on.
/// </summary>
/// <param name="Subject">The provider's subject.</param>
/// <param name="Email">The email.</param>
/// <param name="LoginName">The login name.</param>
/// <param name="Upn">The user principal name.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="IdToken">The raw id_token, kept server-side for <c>id_token_hint</c> on logout. Never logged.</param>
public sealed record OidcIdentity(string Subject, string? Email, string? LoginName, string? Upn, string? DisplayName, string IdToken)
{
    /// <summary>The subject only; the token and personal claims are not printed.</summary>
    public override string ToString() => $"{nameof(OidcIdentity)} {{ {nameof(Subject)}: {Subject} }}";
}

/// <summary>The callback's status, and the identity (on success) or the reason (otherwise).</summary>
public sealed record OidcSignInResult(OidcSignInStatus Status, OidcIdentity? Identity, OidcFailureReason? Failure)
{
    /// <summary>A successful sign-in.</summary>
    public static OidcSignInResult Succeeded(OidcIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new OidcSignInResult(OidcSignInStatus.Succeeded, identity, Failure: null);
    }

    /// <summary>A refused callback.</summary>
    public static OidcSignInResult Rejected(OidcFailureReason reason) => new(OidcSignInStatus.Rejected, Identity: null, reason);

    /// <summary>A provider that could not be asked.</summary>
    public static OidcSignInResult Unavailable() =>
        new(OidcSignInStatus.Unavailable, Identity: null, OidcFailureReason.ProviderUnavailable);
}

using Microsoft.IdentityModel.Tokens;

namespace Codelab.Identity.Oidc;

/// <summary>
/// Bound from <see cref="SectionName"/>: the OpenID Connect provider a host signs users in through.
/// <para>
/// <b>Every endpoint is explicit.</b> A production backend zone usually has no egress to fetch a discovery document,
/// and an unreachable discovery endpoint would take every sign-in down with it; the endpoints are configuration, and
/// phase 8 can route the back channel through the integration gateway by replacing the named client's handler.
/// </para>
/// <para><see cref="ClientSecret"/> is a credential: it has no committed value and arrives from the environment or a secret store.</para>
/// </summary>
public sealed class OidcOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Codelab:Identity:Sso";

    /// <summary>The default tolerance for clock disagreement on <c>exp</c>, <c>nbf</c> and <c>iat</c>, in seconds.</summary>
    public const int DefaultClockSkewSeconds = 120;

    /// <summary>The default timeout of each back-channel call (token, JWKS), in seconds.</summary>
    public const int DefaultBackchannelTimeoutSeconds = 15;

    /// <summary>Whether the provider is on. Off: nothing else here is read.</summary>
    public bool Enabled { get; set; }

    /// <summary>The issuer identifier. The id_token's <c>iss</c> must equal it exactly (no trailing-slash folding).</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>This application's client id; the id_token's <c>aud</c> must contain it.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The client secret, sent as <c>client_secret_post</c> on the token request. Blank: a public client (PKCE only).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>The redirect URI registered at the provider, as the browser sees it. Sent on both the authorization and the token request.</summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>The post-logout redirect URI registered at the provider. Optional; sent on end_session when set.</summary>
    public string PostLogoutRedirectUri { get; set; } = string.Empty;

    /// <summary>The scopes requested. Empty means <see cref="DefaultScopes"/>. Must include <c>openid</c>.</summary>
    public IList<string> Scopes { get; set; } = new List<string>();

    /// <summary>Where the browser signs in (authorization code + PKCE S256).</summary>
    public string AuthorizationEndpoint { get; set; } = string.Empty;

    /// <summary>Where the server redeems the code.</summary>
    public string TokenEndpoint { get; set; } = string.Empty;

    /// <summary>Where the browser ends the provider session (RP-initiated logout).</summary>
    public string EndSessionEndpoint { get; set; } = string.Empty;

    /// <summary>The provider's JSON Web Key Set (used when <see cref="SigningKeyMode"/> is <see cref="Oidc.SigningKeyMode.Jwks"/>).</summary>
    public string JwksUri { get; set; } = string.Empty;

    /// <summary>Where the id_token signing keys come from.</summary>
    public SigningKeyMode SigningKeyMode { get; set; } = SigningKeyMode.Jwks;

    /// <summary>The pinned public keys (used when <see cref="SigningKeyMode"/> is <see cref="Oidc.SigningKeyMode.Pinned"/>).</summary>
    public IList<OidcPinnedKey> PinnedKeys { get; set; } = new List<OidcPinnedKey>();

    /// <summary>The accepted id_token algorithms. Empty means <see cref="OidcAlgorithms.Default"/> (RS256). Only asymmetric algorithms are ever accepted.</summary>
    public IList<string> AllowedAlgorithms { get; set; } = new List<string>();

    /// <summary>The clock-skew tolerance, in seconds.</summary>
    public int ClockSkewSeconds { get; set; } = DefaultClockSkewSeconds;

    /// <summary>The timeout of each back-channel call, in seconds.</summary>
    public int BackchannelTimeoutSeconds { get; set; } = DefaultBackchannelTimeoutSeconds;

    /// <summary>Which identity value finds the local account (<see cref="LoginMatchField.Email"/> by default).</summary>
    public LoginMatchField MatchBy { get; set; } = LoginMatchField.Email;

    /// <summary>
    /// Whether the email is released only when the token asserts <c>email_verified</c>. Default true (fail closed): an
    /// unverified email that finds an account could claim somebody else's.
    /// </summary>
    public bool RequireVerifiedEmail { get; set; } = true;

    /// <summary>Which claims carry the identity values (provider-specific names are configuration, not code).</summary>
    public OidcClaimMap Claims { get; set; } = new();

    /// <summary>The scopes requested when <see cref="Scopes"/> is empty.</summary>
    public static IReadOnlyList<string> DefaultScopes { get; } = [OidcProtocol.OpenIdScope, OidcProtocol.ProfileScope, OidcProtocol.EmailScope];

    /// <summary>The scopes actually requested.</summary>
    public IReadOnlyList<string> EffectiveScopes => Scopes.Count == 0 ? DefaultScopes : [.. Scopes];

    /// <summary>The algorithms actually accepted.</summary>
    public IReadOnlyList<string> EffectiveAllowedAlgorithms => AllowedAlgorithms.Count == 0 ? OidcAlgorithms.Default : [.. AllowedAlgorithms];

    /// <summary><see cref="ClockSkewSeconds"/> as a time span.</summary>
    public TimeSpan ClockSkew => TimeSpan.FromSeconds(ClockSkewSeconds);

    /// <summary><see cref="BackchannelTimeoutSeconds"/> as a time span.</summary>
    public TimeSpan BackchannelTimeout => TimeSpan.FromSeconds(BackchannelTimeoutSeconds);
}

/// <summary>Where id_token signing keys come from. Stored and bound as an integer.</summary>
public enum SigningKeyMode
{
    /// <summary>The provider's JWKS endpoint, fetched over the back channel and cached.</summary>
    Jwks = 1,

    /// <summary>Public keys pinned in configuration (no back-channel fetch; a key rotation is a configuration change).</summary>
    Pinned = 2,
}

/// <summary>One pinned id_token signing key.</summary>
public sealed class OidcPinnedKey
{
    /// <summary>The key's <c>kid</c> as the provider publishes it. Optional: blank keys are tried for any token.</summary>
    public string Kid { get; set; } = string.Empty;

    /// <summary>An RSA (2048 bits or more) or EC public key: SubjectPublicKeyInfo PEM (<c>BEGIN PUBLIC KEY</c>) or its base64 DER.</summary>
    public string PublicKey { get; set; } = string.Empty;
}

/// <summary>The claim names the identity values are read from. Defaults are the OIDC standard claims.</summary>
public sealed class OidcClaimMap
{
    /// <summary>The email claim.</summary>
    public string Email { get; set; } = OidcClaimNames.Email;

    /// <summary>The email-verified claim (a JSON boolean or the string <c>true</c>).</summary>
    public string EmailVerified { get; set; } = OidcClaimNames.EmailVerified;

    /// <summary>The login-name claim (some providers release the directory logon name as <c>loginName</c>).</summary>
    public string LoginName { get; set; } = OidcClaimNames.PreferredUserName;

    /// <summary>The user principal name claim.</summary>
    public string Upn { get; set; } = OidcClaimNames.Upn;

    /// <summary>The display name claim.</summary>
    public string DisplayName { get; set; } = OidcClaimNames.Name;
}

/// <summary>The OIDC claim names this package knows (OpenID Connect Core 1.0 section 5.1 and 2).</summary>
public static class OidcClaimNames
{
    /// <summary>The subject.</summary>
    public const string Subject = "sub";

    /// <summary>The nonce echoed from the authorization request.</summary>
    public const string Nonce = "nonce";

    /// <summary>The authorized party.</summary>
    public const string AuthorizedParty = "azp";

    /// <summary>The expiry (seconds since the epoch).</summary>
    public const string Expires = "exp";

    /// <summary>Not before (seconds since the epoch).</summary>
    public const string NotBefore = "nbf";

    /// <summary>Issued at (seconds since the epoch).</summary>
    public const string IssuedAt = "iat";

    /// <summary>The email.</summary>
    public const string Email = "email";

    /// <summary>Whether the email is verified.</summary>
    public const string EmailVerified = "email_verified";

    /// <summary>The preferred user name.</summary>
    public const string PreferredUserName = "preferred_username";

    /// <summary>The user principal name (not an OIDC standard claim; common with directory-backed providers).</summary>
    public const string Upn = "upn";

    /// <summary>The full name.</summary>
    public const string Name = "name";
}

/// <summary>
/// The id_token algorithms this package can accept: asymmetric only. <c>none</c> and every HMAC algorithm are absent,
/// so a token signed with the client secret, or with an RSA public key used as an HMAC secret, never verifies.
/// </summary>
public static class OidcAlgorithms
{
    /// <summary>The default allowlist: RS256 only.</summary>
    public static IReadOnlyList<string> Default { get; } = [SecurityAlgorithms.RsaSha256];

    /// <summary>Every algorithm that may appear in <see cref="OidcOptions.AllowedAlgorithms"/>.</summary>
    public static IReadOnlyList<string> Asymmetric { get; } =
    [
        SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.RsaSsaPssSha384, SecurityAlgorithms.RsaSsaPssSha512,
        SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512,
    ];

    /// <summary>Whether <paramref name="algorithm"/> is an asymmetric algorithm this package accepts (exact, case-sensitive).</summary>
    public static bool IsAsymmetric(string? algorithm) => algorithm is not null && Asymmetric.Contains(algorithm, StringComparer.Ordinal);
}

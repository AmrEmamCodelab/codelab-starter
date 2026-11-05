using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Codelab.Identity.Oidc;

/// <summary>The OAuth 2.0 / OpenID Connect parameter names and fixed values this package sends.</summary>
public static class OidcProtocol
{
    /// <summary>The scope that makes a request OpenID Connect.</summary>
    public const string OpenIdScope = "openid";

    /// <summary>The profile scope.</summary>
    public const string ProfileScope = "profile";

    /// <summary>The email scope.</summary>
    public const string EmailScope = "email";

    /// <summary>The authorization code response type.</summary>
    public const string CodeResponseType = "code";

    /// <summary>The PKCE challenge method (RFC 7636 4.2); <c>plain</c> is never sent.</summary>
    public const string S256 = "S256";

    /// <summary>The token request grant type.</summary>
    public const string AuthorizationCodeGrant = "authorization_code";

    internal const string ResponseType = "response_type";
    internal const string ClientId = "client_id";
    internal const string ClientSecret = "client_secret";
    internal const string RedirectUri = "redirect_uri";
    internal const string Scope = "scope";
    internal const string State = "state";
    internal const string Nonce = "nonce";
    internal const string CodeChallenge = "code_challenge";
    internal const string CodeChallengeMethod = "code_challenge_method";
    internal const string GrantType = "grant_type";
    internal const string Code = "code";
    internal const string CodeVerifier = "code_verifier";
    internal const string IdTokenHint = "id_token_hint";
    internal const string PostLogoutRedirectUri = "post_logout_redirect_uri";
    internal const string IdToken = "id_token";
    internal const string Error = "error";
    internal const string JsonMediaType = "application/json";
    internal const char ScopeSeparator = ' ';
}

/// <summary>
/// PKCE (RFC 7636) and the opaque single-use values of a sign-in. Every value is 256 bits from the system CSPRNG,
/// base64url without padding (43 characters, all in the RFC 7636 unreserved set).
/// </summary>
public static class Pkce
{
    /// <summary>The random bytes behind each verifier, state and nonce.</summary>
    public const int EntropyBytes = 32;

    /// <summary>A new code verifier.</summary>
    public static string NewVerifier() => NewOpaqueValue();

    /// <summary>A new state or nonce.</summary>
    public static string NewOpaqueValue() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(EntropyBytes));

    /// <summary>The S256 challenge of <paramref name="verifier"/>: <c>BASE64URL(SHA256(ASCII(verifier)))</c>.</summary>
    public static string ChallengeOf(string verifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(verifier);
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    /// <summary>Compares two single-use values in constant time (null never matches).</summary>
    public static bool FixedTimeEquals(string? left, string? right) =>
        left is not null && right is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}

/// <summary>The return-path filter: a value that fails it is never written into a <c>Location</c> header.</summary>
public static class ReturnUrls
{
    /// <summary>Where a refused return path goes.</summary>
    public const string Root = "/";

    private const char Slash = '/';
    private const char Backslash = '\\';
    private const string SchemeSeparator = "://";

    /// <summary>
    /// A local, root-relative path: one leading <c>/</c>, not <c>//</c> or <c>/\</c> (browsers read both as another host),
    /// no scheme and no control character.
    /// </summary>
    public static bool IsRootRelativePath(string? value) =>
        !string.IsNullOrEmpty(value)
        && value[0] == Slash
        && (value.Length == 1 || (value[1] != Slash && value[1] != Backslash))
        && !value.Contains(SchemeSeparator, StringComparison.Ordinal)
        && !value.Any(char.IsControl);

    /// <summary><paramref name="value"/> when it is root-relative, otherwise <see cref="Root"/>.</summary>
    public static string Sanitize(string? value) => IsRootRelativePath(value) ? value! : Root;
}

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Codelab.Identity.Oidc;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Codelab.Identity.Tests.TestSupport;

/// <summary>
/// An in-test OpenID Connect issuer: an RSA key generated at run time, id_tokens signed with it, and its JWKS document.
/// Nothing here is a real provider or a checked-in key.
/// </summary>
internal sealed class TestIssuer : IDisposable
{
    public const string Authority = "https://idp.example.org";
    public const string ClientId = "starter-web";
    public const string RedirectUri = "https://app.example.org/api/auth/sso/signin-oidc";
    public const string AuthorizationEndpoint = "https://idp.example.org/authorize";
    public const string TokenEndpoint = "https://idp.example.org/token";
    public const string EndSessionEndpoint = "https://idp.example.org/end-session";
    public const string JwksUri = "https://idp.example.org/jwks";
    public const string Nonce = "nonce-for-this-sign-in";
    public const string Subject = "subject-0001";

    private const int KeyBits = 2048;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public TestIssuer(TimeProvider clock, string kid = "key-1")
    {
        Clock = clock;
        Rsa = RSA.Create(KeyBits);
        Key = new RsaSecurityKey(Rsa) { KeyId = kid };
    }

    public TimeProvider Clock { get; }

    public RSA Rsa { get; }

    public RsaSecurityKey Key { get; }

    public static OidcOptions Options(SigningKeyMode mode = SigningKeyMode.Jwks) => new()
    {
        Enabled = true,
        Authority = Authority,
        ClientId = ClientId,
        RedirectUri = RedirectUri,
        AuthorizationEndpoint = AuthorizationEndpoint,
        TokenEndpoint = TokenEndpoint,
        EndSessionEndpoint = EndSessionEndpoint,
        JwksUri = JwksUri,
        SigningKeyMode = mode,
    };

    /// <summary>The standard claims of a valid token for <see cref="Nonce"/>, overridable per test (a null value removes the claim).</summary>
    public Dictionary<string, object?> Claims(Action<Dictionary<string, object?>>? change = null)
    {
        var now = Clock.GetUtcNow();
        var claims = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["iss"] = Authority,
            ["aud"] = ClientId,
            [OidcClaimNames.Subject] = Subject,
            [OidcClaimNames.Nonce] = Nonce,
            [OidcClaimNames.IssuedAt] = now.ToUnixTimeSeconds(),
            [OidcClaimNames.NotBefore] = now.ToUnixTimeSeconds(),
            [OidcClaimNames.Expires] = (now + Lifetime).ToUnixTimeSeconds(),
            [OidcClaimNames.Email] = "user.one@example.org",
            [OidcClaimNames.EmailVerified] = true,
            [OidcClaimNames.PreferredUserName] = "user.one",
            [OidcClaimNames.Upn] = "user.one@example.org",
            [OidcClaimNames.Name] = "User One",
        };
        change?.Invoke(claims);
        return claims;
    }

    /// <summary>A token signed RS256 with this issuer's key.</summary>
    public string Token(Action<Dictionary<string, object?>>? change = null) => Sign(Claims(change), new SigningCredentials(Key, SecurityAlgorithms.RsaSha256));

    /// <summary>A token signed with <paramref name="credentials"/> (another key, another algorithm).</summary>
    public static string Sign(Dictionary<string, object?> claims, SigningCredentials credentials)
    {
        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
        var payload = claims.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal);
        return handler.CreateToken(new SecurityTokenDescriptor { Claims = payload, SigningCredentials = credentials });
    }

    /// <summary>An unsigned token (<c>alg: none</c>, empty signature).</summary>
    public string Unsigned(Action<Dictionary<string, object?>>? change = null)
    {
        var header = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}"));
        var body = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(Claims(change).Where(pair => pair.Value is not null).ToDictionary()));
        return $"{header}.{body}.";
    }

    /// <summary>This issuer's JWKS (plus any extra JWK objects).</summary>
    public string Jwks(params object[] extraKeys) => JwksOf([JwkOf(Rsa, Key.KeyId), .. extraKeys]);

    public static string JwksOf(IEnumerable<object> keys) => JsonSerializer.Serialize(new { keys });

    public static object JwkOf(RSA rsa, string? kid)
    {
        var parameters = rsa.ExportParameters(includePrivateParameters: false);
        return new
        {
            kty = "RSA",
            use = "sig",
            alg = SecurityAlgorithms.RsaSha256,
            kid,
            n = Base64Url.EncodeToString(parameters.Modulus),
            e = Base64Url.EncodeToString(parameters.Exponent),
        };
    }

    /// <summary>This issuer's public key as SubjectPublicKeyInfo PEM.</summary>
    public string PublicKeyPem() => Rsa.ExportSubjectPublicKeyInfoPem();

    public void Dispose() => Rsa.Dispose();
}

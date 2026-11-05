using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Starter.DevIdp.Keys;

/// <summary>
/// The RS256 signing key: an RSA key generated in memory when the IdP starts and lost when it stops, so nothing is
/// ever written to disk and every start publishes a new <c>kid</c>. One singleton per host.
/// </summary>
internal sealed class DevIdpSigningKey : IDisposable
{
    /// <summary>The RSA modulus size.</summary>
    public const int KeySizeBits = 2048;

    /// <summary>The only signing algorithm.</summary>
    public const string Algorithm = SecurityAlgorithms.RsaSha256;

    private const int KeyIdBytes = 16;

    private readonly RSA _rsa;

    /// <summary>Generates a fresh key pair and a random key id.</summary>
    public DevIdpSigningKey()
    {
        _rsa = RSA.Create(KeySizeBits);
        KeyId = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(KeyIdBytes));
        var privateKey = new RsaSecurityKey(_rsa) { KeyId = KeyId };
        Credentials = new SigningCredentials(privateKey, Algorithm);
        PublicKey = new RsaSecurityKey(_rsa.ExportParameters(includePrivateParameters: false)) { KeyId = KeyId };
    }

    /// <summary>The key id (<c>kid</c>) in token headers and the JWKS.</summary>
    public string KeyId { get; }

    /// <summary>The credentials that sign id_tokens.</summary>
    public SigningCredentials Credentials { get; }

    /// <summary>The public half, which validates id_token hints.</summary>
    public RsaSecurityKey PublicKey { get; }

    /// <summary>The public key as a JWK (kty, use, alg, kid, n, e).</summary>
    public JwksKey ToJwk()
    {
        var parameters = PublicKey.Parameters;
        return new JwksKey
        {
            KeyType = JsonWebAlgorithmsKeyTypes.RSA,
            Use = JsonWebKeyUseNames.Sig,
            Algorithm = Algorithm,
            KeyId = KeyId,
            Modulus = Base64UrlEncoder.Encode(parameters.Modulus),
            Exponent = Base64UrlEncoder.Encode(parameters.Exponent),
        };
    }

    /// <inheritdoc />
    public void Dispose() => _rsa.Dispose();
}

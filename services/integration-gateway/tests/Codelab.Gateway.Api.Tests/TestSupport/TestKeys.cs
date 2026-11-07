using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Codelab.Gateway.Api.Tests.TestSupport;

/// <summary>Key material and certificates generated per run (never committed, never a published fixture).</summary>
internal static class TestKeys
{
    private const int RandomBytes = 32;
    private const int RsaKeyBits = 2048;
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    /// <summary>A fresh ES256 (P-256) key pair as PEM: PKCS#8 private, SubjectPublicKeyInfo public.</summary>
    public static (string PrivatePem, string PublicPem) NewServiceKeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportPkcs8PrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }

    /// <summary>A random secret (hex), for example a provider credential.</summary>
    public static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(RandomBytes));

    /// <summary>
    /// A self-signed server certificate for 127.0.0.1 and localhost, valid one day either side of now. Exported and
    /// re-imported as PKCS#12 so the Windows TLS stack can use its private key.
    /// </summary>
    public static X509Certificate2 NewLoopbackCertificate()
    {
        using var key = RSA.Create(RsaKeyBits);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuthenticationOid)], critical: false));
        var now = TimeProvider.System.GetUtcNow();
        using var created = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12), password: null);
    }

    /// <summary>The SHA-256 thumbprint of <paramref name="certificate"/> as upper-case hex.</summary>
    public static string Sha256Thumbprint(X509Certificate2 certificate) => Convert.ToHexString(SHA256.HashData(certificate.RawData));
}

/// <summary>Credentials of other schemes, built at run time: the gateway must ignore every one of them.</summary>
internal static class HttpCredentials
{
    public const string ApiKeyHeader = "Api-Key";

    private const int RandomBytes = 32;

    /// <summary>A well-formed HS256 JWT with a random signature and a service's claims.</summary>
    public static readonly string UnsignedJwt = string.Join(
        '.',
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" })),
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new { sub = "starter-api", aud = "integration-gateway" })),
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RandomBytes)));

    /// <summary>A random API key.</summary>
    public static readonly string ApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(RandomBytes));
}

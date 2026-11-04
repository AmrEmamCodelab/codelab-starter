using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Codelab.Integration.Tests.Support;

/// <summary>
/// Certificates generated at run time for the TLS tests (ECDSA P-256). Nothing is written to disk or to a certificate
/// store, and no key material is checked in.
/// </summary>
internal static class TestCertificates
{
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
    private const int SerialNumberLength = 16;

    // Authorities outlive their servers, so a server issued in the past (an expired one) still fits inside them.
    private static readonly TimeSpan AuthorityLifetime = TimeSpan.FromDays(1000);
    private static readonly TimeSpan ServerLifetime = TimeSpan.FromDays(1);

    public static X509Certificate2 CreateAuthority(string commonName, TimeProvider? clock = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
        var (notBefore, notAfter) = ValidityWindow(clock, AuthorityLifetime);
        using var authority = request.CreateSelfSigned(notBefore, notAfter);
        return Reimport(authority);
    }

    public static X509Certificate2 CreateServer(X509Certificate2 issuer, TimeProvider? clock = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = ServerRequest(key);
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer, includeKeyIdentifier: true, includeIssuerAndSerial: false));
        var (notBefore, notAfter) = ValidityWindow(clock, ServerLifetime);
        using var issued = request.Create(issuer, notBefore, notAfter, RandomNumberGenerator.GetBytes(SerialNumberLength));
        using var withKey = issued.CopyWithPrivateKey(key);
        return Reimport(withKey);
    }

    public static X509Certificate2 CreateSelfSignedServer(TimeProvider? clock = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = ServerRequest(key);
        var (notBefore, notAfter) = ValidityWindow(clock, ServerLifetime);
        using var server = request.CreateSelfSigned(notBefore, notAfter);
        return Reimport(server);
    }

    /// <summary>The public part only, as a client would hold a pinned CA.</summary>
    public static X509Certificate2 PublicPart(X509Certificate2 certificate) => X509CertificateLoader.LoadCertificate(certificate.RawData);

    private static CertificateRequest ServerRequest(ECDsa key)
    {
        var request = new CertificateRequest("CN=outbound-test-server", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuthenticationOid)], critical: false));
        return request;
    }

    private static (DateTimeOffset NotBefore, DateTimeOffset NotAfter) ValidityWindow(TimeProvider? clock, TimeSpan halfLife)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        return (now - halfLife, now + halfLife);
    }

    // Schannel (Windows) cannot serve TLS with an ephemeral key; a PKCS#12 round trip gives the key a container that
    // is removed again when the certificate is disposed.
    private static X509Certificate2 Reimport(X509Certificate2 certificate) =>
        X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), password: null);
}

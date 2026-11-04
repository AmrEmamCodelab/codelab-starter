using System.Security.Cryptography.X509Certificates;

namespace Codelab.Integration.Certificates;

/// <summary>How an outbound client trusts the server certificate. Validation is always on.</summary>
public sealed class OutboundCertificateOptions
{
    /// <summary>The trust anchor. Default: the operating-system trust store.</summary>
    public CertificateTrustMode Mode { get; set; } = CertificateTrustMode.SystemTrust;

    /// <summary>
    /// <see cref="CertificateTrustMode.PinnedAuthority"/>: a PEM file holding the internal CA certificate(s) the
    /// server chain must end in. Read when the client's handler is created.
    /// </summary>
    public string? AuthorityBundlePath { get; set; }

    /// <summary>
    /// <see cref="CertificateTrustMode.PinnedAuthority"/>: CA certificates supplied in code (for example loaded from a
    /// certificate store by the host). Combined with <see cref="AuthorityBundlePath"/>.
    /// </summary>
    public X509Certificate2Collection AuthorityCertificates { get; } = [];

    /// <summary>
    /// <see cref="CertificateTrustMode.PinnedLeaf"/>: SHA-256 thumbprints (64 hex characters, case and ':'
    /// separators ignored) of the server certificates this client accepts.
    /// </summary>
    public IList<string> PinnedThumbprints { get; } = [];

    /// <summary>
    /// Checks revocation online while building the chain. Off by default, like .NET itself, because a backend zone
    /// without egress usually cannot reach a CRL or OCSP endpoint; turn it on where the CA publishes one internally.
    /// </summary>
    public bool CheckRevocation { get; set; }
}

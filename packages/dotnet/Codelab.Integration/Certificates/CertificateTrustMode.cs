namespace Codelab.Integration.Certificates;

/// <summary>
/// The trust anchor an outbound client validates the server certificate against. There is no "accept anything"
/// mode: every mode checks the host name, and every mode refuses a certificate it cannot tie to its anchor.
/// </summary>
public enum CertificateTrustMode
{
    /// <summary>The operating-system trust store (default chain validation and no custom callback at all).</summary>
    SystemTrust = 1,

    /// <summary>Only the configured internal CA bundle; the system roots are not trusted for this client.</summary>
    PinnedAuthority = 2,

    /// <summary>Only server certificates whose SHA-256 thumbprint is pinned; validity dates are still checked.</summary>
    PinnedLeaf = 3,
}

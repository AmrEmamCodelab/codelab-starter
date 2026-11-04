using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Codelab.Integration.Certificates;

/// <summary>
/// Accepts a server certificate only when its SHA-256 thumbprint is pinned, it names the host, and it is inside its
/// validity period. Chain errors are excused (a pinned leaf is often self-signed); nothing else is.
/// </summary>
internal sealed class PinnedLeafValidator(IReadOnlySet<string> pinnedThumbprints, TimeProvider timeProvider)
    : IServerCertificateValidator
{
    public bool Validate(X509Certificate? certificate, X509Chain? presentedChain, SslPolicyErrors errors)
    {
        if (certificate is null || HandshakeErrors.HasFatalError(errors))
        {
            return false;
        }

        if (!pinnedThumbprints.Contains(Thumbprints.Sha256Of(certificate)))
        {
            return false;
        }

        if (certificate is X509Certificate2 leaf)
        {
            return IsWithinValidity(leaf);
        }

        using var copy = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        return IsWithinValidity(copy);
    }

    private bool IsWithinValidity(X509Certificate2 certificate)
    {
        var now = timeProvider.GetUtcNow();
        return now >= new DateTimeOffset(certificate.NotBefore) && now <= new DateTimeOffset(certificate.NotAfter);
    }
}

using System.Net.Security;

namespace Codelab.Integration.Certificates;

/// <summary>The handshake errors no trust anchor can excuse.</summary>
internal static class HandshakeErrors
{
    private const SslPolicyErrors AlwaysFatal =
        SslPolicyErrors.RemoteCertificateNotAvailable | SslPolicyErrors.RemoteCertificateNameMismatch;

    /// <summary>
    /// True when the server sent no certificate or the certificate does not name the host. A pinned anchor replaces
    /// only the chain decision; it never accepts a certificate issued for another host.
    /// </summary>
    public static bool HasFatalError(SslPolicyErrors errors) => (errors & AlwaysFatal) != SslPolicyErrors.None;
}

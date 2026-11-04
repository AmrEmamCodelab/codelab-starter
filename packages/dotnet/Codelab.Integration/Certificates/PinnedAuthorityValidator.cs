using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Codelab.Integration.Certificates;

/// <summary>
/// Accepts a server certificate only when its chain builds to one of the pinned internal CA certificates. The system
/// roots are not trusted (<see cref="X509ChainTrustMode.CustomRootTrust"/>), the certificate must allow TLS server
/// authentication, and the host name must match.
/// </summary>
internal sealed class PinnedAuthorityValidator(X509Certificate2Collection authorities, X509RevocationMode revocationMode)
    : IServerCertificateValidator
{
    public bool Validate(X509Certificate? certificate, X509Chain? presentedChain, SslPolicyErrors errors)
    {
        if (certificate is null || HandshakeErrors.HasFatalError(errors) || authorities.Count == 0)
        {
            return false;
        }

        using var chain = CreateChain(presentedChain);
        if (certificate is X509Certificate2 leaf)
        {
            return chain.Build(leaf);
        }

        using var copy = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        return chain.Build(copy);
    }

    private X509Chain CreateChain(X509Chain? presentedChain)
    {
        var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(authorities);
        chain.ChainPolicy.RevocationMode = revocationMode;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid(CertificateConstants.ServerAuthenticationOid));
        AddIntermediates(chain, presentedChain);
        return chain;
    }

    // The server may send intermediates; they help build the chain but are never trusted as anchors.
    private static void AddIntermediates(X509Chain chain, X509Chain? presentedChain)
    {
        if (presentedChain is null)
        {
            return;
        }

        foreach (var element in presentedChain.ChainElements)
        {
            chain.ChainPolicy.ExtraStore.Add(element.Certificate);
        }
    }
}

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Codelab.Integration.Certificates;

/// <summary>A pinned trust check run inside the TLS handshake.</summary>
internal interface IServerCertificateValidator
{
    /// <summary>True only when the presented certificate is tied to this validator's trust anchor.</summary>
    bool Validate(X509Certificate? certificate, X509Chain? presentedChain, SslPolicyErrors errors);
}

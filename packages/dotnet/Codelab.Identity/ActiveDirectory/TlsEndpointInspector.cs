using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Codelab.Identity.ActiveDirectory;

/// <summary>The certificate a directory presented on its LDAPS port. Diagnostic only; no key material.</summary>
/// <param name="Subject">The subject distinguished name.</param>
/// <param name="Issuer">The issuer distinguished name.</param>
/// <param name="Thumbprint">The SHA-256 thumbprint, upper-case hexadecimal.</param>
/// <param name="NotBefore">The start of validity (UTC).</param>
/// <param name="NotAfter">The end of validity (UTC); the date an operator watches.</param>
/// <param name="PolicyErrors">What this host's validation found (<see cref="SslPolicyErrors.None"/> when it trusts it).</param>
public sealed record LdapsCertificateInfo(
    string Subject,
    string Issuer,
    string Thumbprint,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    SslPolicyErrors PolicyErrors)
{
    /// <summary>Whether this host validates the certificate (chain and name). A sign-in over LDAPS needs it.</summary>
    public bool IsTrusted => PolicyErrors == SslPolicyErrors.None;
}

/// <summary>What one look at an endpoint found, layer by layer. Nothing is sent: no bind, no DN, no password.</summary>
internal sealed record EndpointInspection(bool TcpConnected, string? Failure, LdapsCertificateInfo? Certificate, bool TlsEstablished);

/// <summary>
/// A TCP connect and, for LDAPS, a TLS handshake with NORMAL certificate validation, keeping what the server presented.
/// The validation callback records the certificate and returns the platform's own verdict, so an untrusted server is
/// still refused (the handshake fails) and its certificate is still reported. Shared by the certificate reader and the probe.
/// </summary>
internal static class TlsEndpointInspector
{
    internal static async Task<EndpointInspection> InspectAsync(LdapEndpoint endpoint, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        using var budget = new CancellationTokenSource(endpoint.Timeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        using var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(endpoint.Host, endpoint.Port, linked.Token);
        }
        catch (Exception failure) when (IsConnectFailure(failure, cancellationToken))
        {
            return new EndpointInspection(TcpConnected: false, Failure: Describe(failure), Certificate: null, TlsEstablished: false);
        }

        return endpoint.UseLdaps
            ? await HandshakeAsync(tcp, endpoint.Host, linked.Token, cancellationToken)
            : new EndpointInspection(TcpConnected: true, Failure: null, Certificate: null, TlsEstablished: false);
    }

    private static async Task<EndpointInspection> HandshakeAsync(TcpClient tcp, string host, CancellationToken bounded, CancellationToken caller)
    {
        LdapsCertificateInfo? presented = null;
        await using var tls = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, (_, certificate, _, errors) =>
        {
            presented = certificate is null ? null : InfoOf(certificate, errors);
            return errors == SslPolicyErrors.None;
        });
        try
        {
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, bounded);
            return new EndpointInspection(TcpConnected: true, Failure: null, Certificate: presented, TlsEstablished: true);
        }
        catch (Exception failure) when (IsHandshakeFailure(failure, caller))
        {
            return new EndpointInspection(TcpConnected: true, Failure: Describe(failure), Certificate: presented, TlsEstablished: false);
        }
    }

    private static LdapsCertificateInfo InfoOf(X509Certificate certificate, SslPolicyErrors errors)
    {
        using var full = new X509Certificate2(certificate);
        return new LdapsCertificateInfo(
            Subject: full.Subject,
            Issuer: full.Issuer,
            Thumbprint: full.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256),
            NotBefore: new DateTimeOffset(full.NotBefore.ToUniversalTime()),
            NotAfter: new DateTimeOffset(full.NotAfter.ToUniversalTime()),
            PolicyErrors: errors);
    }

    private static bool IsConnectFailure(Exception failure, CancellationToken caller) =>
        failure is SocketException || (failure is OperationCanceledException && !caller.IsCancellationRequested);

    private static bool IsHandshakeFailure(Exception failure, CancellationToken caller) =>
        failure is AuthenticationException or IOException || (failure is OperationCanceledException && !caller.IsCancellationRequested);

    /// <summary>The failure's kind, never its payload: a socket error code or the exception type.</summary>
    private static string Describe(Exception failure) => failure switch
    {
        SocketException socket => socket.SocketErrorCode.ToString(),
        OperationCanceledException => nameof(TimeoutException),
        _ => failure.GetType().Name,
    };
}

using System.Net.Security;
using Codelab.Identity.ActiveDirectory;
using Codelab.Identity.Tests.TestSupport;
using Microsoft.Extensions.Options;

namespace Codelab.Identity.Tests.ActiveDirectory;

/// <summary>The probe and the certificate reader against loopback listeners (no network beyond this machine).</summary>
public sealed class DirectoryDiagnosticsTests
{
    private const string Loopback = "127.0.0.1";

    private static IOptions<ActiveDirectoryOptions> Settings(int port, bool useLdaps = true, bool enabled = true) =>
        Options.Create(new ActiveDirectoryOptions
        {
            Enabled = enabled,
            Host = Loopback,
            Port = port,
            UseLdaps = useLdaps,
            Domain = "example.org",
            BaseDn = "DC=example,DC=org",
            TimeoutSeconds = 5,
        });

    [Fact]
    public async Task A_closed_port_is_tcp_unreachable()
    {
        var probe = new ActiveDirectoryConnectionProbe(Settings(LoopbackListener.ClosedPort()), TimeProvider.System);

        var result = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(DirectoryProbeStatus.TcpUnreachable, result.Status);
        Assert.Contains($"{Loopback}:", result.Detail, StringComparison.Ordinal);
        Assert.Null(result.Certificate);
    }

    [Fact]
    public async Task An_untrusted_certificate_is_tls_rejected_and_still_reported()
    {
        await using var listener = LoopbackListener.Tls(TimeProvider.System);
        var probe = new ActiveDirectoryConnectionProbe(Settings(listener.Port), TimeProvider.System);

        var result = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(DirectoryProbeStatus.TlsRejected, result.Status);
        Assert.NotNull(result.Certificate);
        Assert.False(result.Certificate.IsTrusted);
        Assert.True(result.Certificate.PolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.Contains("RemoteCertificateChainErrors", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plain_port_is_reachable_when_ldaps_is_off()
    {
        await using var listener = LoopbackListener.Plain();
        var probe = new ActiveDirectoryConnectionProbe(Settings(listener.Port, useLdaps: false), TimeProvider.System);

        var result = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(DirectoryProbeStatus.Reachable, result.Status);
        Assert.Contains("no TLS", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_disabled_provider_is_not_probed()
    {
        var probe = new ActiveDirectoryConnectionProbe(Settings(port: 1, enabled: false), TimeProvider.System);

        var result = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(DirectoryProbeStatus.NotConfigured, result.Status);
    }

    [Fact]
    public async Task The_reader_reports_the_presented_certificate_even_when_untrusted()
    {
        await using var listener = LoopbackListener.Tls(TimeProvider.System);
        var reader = new LdapsCertificateReader(Settings(listener.Port), TimeProvider.System);

        var certificate = await reader.ReadAsync(CancellationToken.None);

        Assert.NotNull(certificate);
        Assert.Equal("CN=directory-test-server", certificate.Subject);
        Assert.Equal(new DateTimeOffset(listener.Certificate!.NotAfter.ToUniversalTime()), certificate.NotAfter);
        Assert.Equal(listener.Certificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256), certificate.Thumbprint);
    }

    [Fact]
    public async Task The_reader_reads_nothing_without_ldaps()
    {
        var reader = new LdapsCertificateReader(Settings(port: 1, useLdaps: false), TimeProvider.System);

        Assert.Null(await reader.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public void Classification_names_the_endpoint_and_the_failure_kind_only()
    {
        var endpoint = new LdapEndpoint("dc1.example.org", 636, UseLdaps: true, TimeSpan.FromSeconds(1));

        var (status, detail) = ActiveDirectoryConnectionProbe.Classify(endpoint, new EndpointInspection(TcpConnected: true, Failure: "AuthenticationException", Certificate: null, TlsEstablished: false));

        Assert.Equal(DirectoryProbeStatus.TlsRejected, status);
        Assert.Equal("TLS to dc1.example.org:636 failed: AuthenticationException.", detail);
    }
}

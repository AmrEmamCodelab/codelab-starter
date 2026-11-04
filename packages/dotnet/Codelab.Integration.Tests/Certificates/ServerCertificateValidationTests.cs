using System.Security.Cryptography.X509Certificates;
using Codelab.Integration.Certificates;
using Codelab.Integration.Classification;
using Codelab.Integration.Outbound;
using Codelab.Integration.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Codelab.Integration.Tests.Certificates;

/// <summary>
/// The fix for "internal certificate validation always passes": real TLS handshakes against an in-process Kestrel
/// server with run-time certificates. Untrusted servers are refused (and not retried); pinned anchors are accepted.
/// </summary>
public sealed class ServerCertificateValidationTests
{
    [Fact]
    public async Task An_untrusted_self_signed_server_is_refused_under_system_trust()
    {
        using var serverCertificate = TestCertificates.CreateSelfSignedServer();
        await using var server = await TlsTestServer.StartAsync(serverCertificate);
        using var provider = OutboundTestHost.Build(options => options.BaseAddress = server.BaseAddress);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => OutboundTestHost.ClientOf(provider).GetAsync(TlsTestServer.PingPath));

        Assert.Equal(OutboundFailureKind.CertificateRejected, OutboundFailureClassifier.Classify(exception).Kind);
        Assert.False(OutboundFailureClassifier.Classify(exception).IsTransient);
    }

    [Fact]
    public async Task A_server_issued_by_the_pinned_authority_is_accepted()
    {
        using var authority = TestCertificates.CreateAuthority("Outbound Test Root");
        using var serverCertificate = TestCertificates.CreateServer(authority);
        await using var server = await TlsTestServer.StartAsync(serverCertificate);
        using var pinned = TestCertificates.PublicPart(authority);
        using var provider = OutboundTestHost.Build(options => PinAuthority(options, server.BaseAddress, pinned));

        var body = await OutboundTestHost.ClientOf(provider).GetStringAsync(TlsTestServer.PingPath);

        Assert.Equal(TlsTestServer.PingBody, body);
    }

    [Fact]
    public async Task A_pinned_authority_bundle_file_is_read_and_trusted()
    {
        using var authority = TestCertificates.CreateAuthority("Outbound Bundle Root");
        using var serverCertificate = TestCertificates.CreateServer(authority);
        await using var server = await TlsTestServer.StartAsync(serverCertificate);
        var bundlePath = Path.Combine(Path.GetTempPath(), $"codelab-outbound-{Guid.NewGuid():N}.pem");
        await File.WriteAllTextAsync(bundlePath, authority.ExportCertificatePem());
        try
        {
            using var provider = OutboundTestHost.Build(options =>
            {
                options.BaseAddress = server.BaseAddress;
                options.Certificate.Mode = CertificateTrustMode.PinnedAuthority;
                options.Certificate.AuthorityBundlePath = bundlePath;
            });

            Assert.Equal(TlsTestServer.PingBody, await OutboundTestHost.ClientOf(provider).GetStringAsync(TlsTestServer.PingPath));
        }
        finally
        {
            File.Delete(bundlePath);
        }
    }

    [Fact]
    public async Task A_server_from_another_authority_is_refused_even_when_an_authority_is_pinned()
    {
        using var pinnedAuthority = TestCertificates.CreateAuthority("Pinned Root");
        using var otherAuthority = TestCertificates.CreateAuthority("Other Root");
        using var serverCertificate = TestCertificates.CreateServer(otherAuthority);
        await using var server = await TlsTestServer.StartAsync(serverCertificate);
        using var pinned = TestCertificates.PublicPart(pinnedAuthority);
        using var provider = OutboundTestHost.Build(options => PinAuthority(options, server.BaseAddress, pinned));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => OutboundTestHost.ClientOf(provider).GetAsync(TlsTestServer.PingPath));

        Assert.Equal(OutboundFailureKind.CertificateRejected, OutboundFailureClassifier.Classify(exception).Kind);
    }

    [Fact]
    public async Task A_pinned_self_signed_leaf_is_accepted_and_an_unpinned_one_is_refused()
    {
        using var serverCertificate = TestCertificates.CreateSelfSignedServer();
        await using var server = await TlsTestServer.StartAsync(serverCertificate);
        using var unrelated = TestCertificates.CreateSelfSignedServer();

        using var pinnedProvider = OutboundTestHost.Build(options => PinLeaf(options, server.BaseAddress, serverCertificate));
        using var wrongPinProvider = OutboundTestHost.Build(options => PinLeaf(options, server.BaseAddress, unrelated));

        Assert.Equal(TlsTestServer.PingBody, await OutboundTestHost.ClientOf(pinnedProvider).GetStringAsync(TlsTestServer.PingPath));
        await Assert.ThrowsAsync<HttpRequestException>(() => OutboundTestHost.ClientOf(wrongPinProvider).GetAsync(TlsTestServer.PingPath));
    }

    [Fact]
    public async Task A_refused_certificate_is_not_retried()
    {
        using var serverCertificate = TestCertificates.CreateSelfSignedServer();
        await using var server = await TlsTestServer.StartAsync(serverCertificate);
        var attempts = new CallCounter();
        using var provider = OutboundTestHost.Build(
            options =>
            {
                options.BaseAddress = server.BaseAddress;
                options.Retry.MaxRetries = 3;
                options.Retry.BaseDelay = TimeSpan.FromMilliseconds(1);
            },
            client: builder => builder.AddHttpMessageHandler(() => new CountingHandler(attempts)));

        await Assert.ThrowsAsync<HttpRequestException>(() => OutboundTestHost.ClientOf(provider).GetAsync(TlsTestServer.PingPath));

        Assert.Equal(1, attempts.Calls);
    }

    private static void PinAuthority(OutboundClientOptions options, Uri address, X509Certificate2 authority)
    {
        options.BaseAddress = address;
        options.Certificate.Mode = CertificateTrustMode.PinnedAuthority;
        options.Certificate.AuthorityCertificates.Add(authority);
    }

    private static void PinLeaf(OutboundClientOptions options, Uri address, X509Certificate2 leaf)
    {
        options.BaseAddress = address;
        options.Certificate.Mode = CertificateTrustMode.PinnedLeaf;
        options.Certificate.PinnedThumbprints.Add(leaf.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256));
    }
}

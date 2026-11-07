using System.Net;
using Codelab.Gateway.Api.Tests.TestSupport;
using Codelab.Integration.Classification;
using Codelab.Integration.Gateway;

namespace Codelab.Gateway.Api.Tests.Http;

/// <summary>
/// Upstream TLS is validated for real: a certificate the policy does not trust stops the relay before any byte of the
/// request leaves, is answered 503 GATEWAY_UPSTREAM_UNAVAILABLE and is logged as CertificateRejected.
/// </summary>
public sealed class UpstreamTlsTests
{
    private const int RelayFailedEventId = 5441;
    private const string Form = "grant_type=authorization_code&code=c0de";

    [Fact]
    public async Task A_self_signed_upstream_is_refused_under_system_trust()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var host = new GatewayHost(upstream, settings =>
        {
            settings["Gateway:Sso:Certificate:Mode"] = "1";
            settings.Remove("Gateway:Sso:Certificate:PinnedThumbprints:0");
        });

        await AssertCertificateRejectedAsync(host, upstream);
    }

    [Fact]
    public async Task An_upstream_with_another_certificate_than_the_pinned_one_is_refused()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        using var other = TestKeys.NewLoopbackCertificate();
        await using var host = new GatewayHost(upstream, settings => settings["Gateway:Sso:Certificate:PinnedThumbprints:0"] = TestKeys.Sha256Thumbprint(other));

        await AssertCertificateRejectedAsync(host, upstream);
    }

    private static async Task AssertCertificateRejectedAsync(GatewayHost host, FakeUpstream upstream)
    {
        using var client = host.CreateClient();

        using var response = await client.SendAsync(host.TokenRequest(Form));

        await Problems.AssertAsync(response, HttpStatusCode.ServiceUnavailable, GatewayFailureCodes.UpstreamUnavailable);
        Assert.Empty(upstream.TokenForms);
        Assert.Contains(host.Logs.Entries, entry => entry.EventId.Id == RelayFailedEventId
            && entry.Message.Contains(nameof(OutboundFailureKind.CertificateRejected), StringComparison.Ordinal));
    }
}

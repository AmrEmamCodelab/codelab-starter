using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Codelab.Security.Problems;
using Codelab.Security.Signing;
using Codelab.Security.Tests.TestSupport;

namespace Codelab.Security.Tests.Signing;

[Collection(SharedCodecGroup.Name)]
public sealed class SigningModesAndKeyEndpointTests
{
    private readonly string _master = TestKeys.NewKey();

    [Fact]
    public async Task Report_mode_lets_an_unsigned_request_through_and_signs_nothing()
    {
        await using var host = await SecurityTestHost.StartAsync(SigningHost.Options(SecurityEnforcementMode.Report, _master));

        var response = await host.Client.SendAsync(SigningHost.Request(HttpMethod.Get, "/api/me"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains(TestProtocols.Test.ResponseSignatureHeaderName));
    }

    [Fact]
    public async Task Off_mode_ignores_proofs_entirely()
    {
        var options = SigningHost.Options(SecurityEnforcementMode.Off, _master);
        await using var host = await SecurityTestHost.StartAsync(options);
        using var request = SigningHost.Request(HttpMethod.Get, "/api/me");
        request.Headers.Add(TestProtocols.Test.SignatureHeaderName, "garbage");

        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.SendAsync(SigningHost.Request(HttpMethod.Get, SigningKeyEndpoint.DefaultPattern))).StatusCode);
    }

    [Fact]
    public async Task The_key_endpoint_issues_the_derived_session_key_unsigned_and_no_store()
    {
        await using var host = await SecurityTestHost.StartAsync(SigningHost.Options(SecurityEnforcementMode.Enforce, _master));

        var response = await host.Client.SendAsync(SigningHost.Request(HttpMethod.Get, SigningKeyEndpoint.DefaultPattern));
        var block = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(["alg", "audience", "serverTimeUnix", "sessionSigningKey", "verifyResponses"], block.Select(property => property.Key).Order(StringComparer.Ordinal));
        Assert.Equal(SigningKeyIssuer.Algorithm, block["alg"]!.GetValue<string>());
        Assert.Equal(SigningHost.Audience, block["audience"]!.GetValue<string>());
        Assert.True(block["verifyResponses"]!.GetValue<bool>());
        Assert.Equal(host.Clock.GetUtcNow().ToUnixTimeSeconds(), block["serverTimeUnix"]!.GetValue<long>());
        Assert.Equal(Convert.ToBase64String(SigningHost.Signer(_master).SessionKey), block["sessionSigningKey"]!.GetValue<string>());
        Assert.NotEqual(_master, block["sessionSigningKey"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_key_endpoint_refuses_anonymous_callers_and_unbound_tokens()
    {
        await using var host = await SecurityTestHost.StartAsync(SigningHost.Options(SecurityEnforcementMode.Enforce, _master));
        using var unbound = new HttpRequestMessage(HttpMethod.Get, SigningKeyEndpoint.DefaultPattern);
        (SigningHost.Session with { SessionKeyId = "retired" }).ApplyTo(unbound);

        var anonymous = await host.Client.GetAsync(SigningKeyEndpoint.DefaultPattern);
        var refused = await host.Client.SendAsync(unbound);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal(SecurityProblemCodes.SignatureKeyUnbound, problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Response_signing_without_request_signing_fails_the_boot()
    {
        var options = SigningHost.Options(SecurityEnforcementMode.Off, _master);
        options.Settings[$"{SecuritySections.ResponseSigning}:Mode"] = nameof(SecurityEnforcementMode.Enforce);

        await BootAssert.FailsAsync(options, "ResponseSigning");
    }

    [Fact]
    public async Task Enforce_on_the_memory_replay_store_outside_development_needs_an_acknowledgement()
    {
        var options = SigningHost.Options(SecurityEnforcementMode.Enforce, _master);
        options.Settings[$"{SecuritySections.ReplayProtection}:AcknowledgeSingleInstance"] = "false";

        await BootAssert.FailsAsync(options, "AcknowledgeSingleInstance");
    }

    [Fact]
    public async Task Signing_without_an_audience_or_with_a_weak_master_fails_the_boot()
    {
        var noAudience = SigningHost.Options(SecurityEnforcementMode.Report, _master);
        noAudience.Settings[$"{SecuritySections.RequestSigning}:Audience"] = string.Empty;
        var weak = SigningHost.Options(SecurityEnforcementMode.Report, Convert.ToBase64String(new byte[16]));

        await BootAssert.FailsAsync(noAudience, "Audience");
        await BootAssert.FailsAsync(weak, "16 bytes");
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using Codelab.Security.Problems;
using Codelab.Security.Signing;
using Codelab.Security.Tests.TestSupport;

namespace Codelab.Security.Tests.Signing;

/// <summary>Request signing in Enforce through a real host: served, tampered, stale, replayed, exempt, oversized.</summary>
[Collection(SharedCodecGroup.Name)]
public sealed class SignedRequestEnforcementTests : IAsyncLifetime
{
    private readonly string _master = TestKeys.NewKey();
    private SecurityTestHost _host = null!;

    private RequestSigner Signer => SigningHost.Signer(_master);

    public async Task InitializeAsync() => _host = await SecurityTestHost.StartAsync(SigningHost.Options(SecurityEnforcementMode.Enforce, _master));

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_correctly_signed_request_is_served_with_a_verifiable_response_signature()
    {
        using var request = SigningHost.Request(HttpMethod.Get, "/api/me?view=full");
        var nonce = Signer.Sign(request, _host.Clock.GetUtcNow());

        var response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await Signer.VerifyResponseAsync(response, nonce));
    }

    [Fact]
    public async Task The_request_body_is_bound_to_the_proof()
    {
        const string body = "{\"value\":1}";
        using var signed = SigningHost.Request(HttpMethod.Post, "/api/echo", body);
        Signer.Sign(signed, _host.Clock.GetUtcNow(), Encoding.UTF8.GetBytes(body));
        using var tampered = SigningHost.Request(HttpMethod.Post, "/api/echo", "{\"value\":2}");
        Signer.Sign(tampered, _host.Clock.GetUtcNow(), Encoding.UTF8.GetBytes(body));

        var served = await _host.Client.SendAsync(signed);

        Assert.Equal(body, await served.Content.ReadAsStringAsync());
        await AssertRefusedAsync(tampered, SecurityProblemCodes.SignatureInvalid);
    }

    [Fact]
    public async Task An_unsigned_authenticated_request_is_refused_as_signature_missing()
    {
        await AssertRefusedAsync(SigningHost.Request(HttpMethod.Get, "/api/me"), SecurityProblemCodes.SignatureMissing);
    }

    [Fact]
    public async Task A_tampered_mac_is_refused()
    {
        using var request = SigningHost.Request(HttpMethod.Get, "/api/me");
        Signer.Sign(request, _host.Clock.GetUtcNow());
        var proof = request.Headers.GetValues(TestProtocols.Test.SignatureHeaderName).Single();
        request.Headers.Remove(TestProtocols.Test.SignatureHeaderName);
        request.Headers.Add(TestProtocols.Test.SignatureHeaderName, proof[..^1] + (proof[^1] == 'A' ? 'Q' : 'A'));

        await AssertRefusedAsync(request, SecurityProblemCodes.SignatureInvalid);
    }

    [Theory]
    [InlineData("/api/me?view=full", "/api/me?view=summary")]
    [InlineData("/api/me", "/api/other")]
    public async Task A_proof_minted_for_a_different_target_is_refused(string signedFor, string sentTo)
    {
        using var minted = SigningHost.Request(HttpMethod.Get, signedFor);
        Signer.Sign(minted, _host.Clock.GetUtcNow());
        using var request = SigningHost.Request(HttpMethod.Get, sentTo);
        request.Headers.Add(TestProtocols.Test.SignatureHeaderName, minted.Headers.GetValues(TestProtocols.Test.SignatureHeaderName));

        var response = await _host.Client.SendAsync(request);

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(-300)]
    [InlineData(300)]
    public async Task A_timestamp_outside_the_window_is_refused_as_stale(int offsetSeconds)
    {
        using var request = SigningHost.Request(HttpMethod.Get, "/api/me");
        Signer.Sign(request, _host.Clock.GetUtcNow().AddSeconds(offsetSeconds));

        await AssertRefusedAsync(request, SecurityProblemCodes.SignatureStale);
    }

    [Fact]
    public async Task A_replayed_proof_is_refused_as_signature_replayed()
    {
        using var first = SigningHost.Request(HttpMethod.Get, "/api/me");
        var nonce = Signer.Sign(first, _host.Clock.GetUtcNow());
        using var replay = SigningHost.Request(HttpMethod.Get, "/api/me");
        Signer.Sign(replay, _host.Clock.GetUtcNow(), nonce: nonce);

        Assert.Equal(HttpStatusCode.OK, (await _host.Client.SendAsync(first)).StatusCode);
        await AssertRefusedAsync(replay, SecurityProblemCodes.SignatureReplayed);
    }

    [Fact]
    public async Task A_token_whose_key_id_left_the_ring_is_key_unbound()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        (SigningHost.Session with { SessionKeyId = "retired" }).ApplyTo(request);
        Signer.Sign(request, _host.Clock.GetUtcNow());

        await AssertRefusedAsync(request, SecurityProblemCodes.SignatureKeyUnbound);
    }

    [Fact]
    public async Task Exempt_endpoints_answer_while_enforcing()
    {
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/login");
        using var upload = SigningHost.Request(HttpMethod.Post, "/api/upload", "multipart-ish bytes the proof does not cover");
        Signer.Sign(upload, _host.Clock.GetUtcNow(), bodyHash: SignatureCanonicalForm.UnsignedBody);

        Assert.Equal(HttpStatusCode.OK, (await _host.Client.SendAsync(login)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _host.Client.SendAsync(upload)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client.SendAsync(SigningHost.Request(HttpMethod.Post, "/api/upload", "x"))).StatusCode);
    }

    [Fact]
    public async Task A_signed_body_past_the_cap_is_refused_413_with_the_code()
    {
        var body = new string('x', 2048);
        using var request = SigningHost.Request(HttpMethod.Post, "/api/echo", body);
        Signer.Sign(request, _host.Clock.GetUtcNow(), Encoding.UTF8.GetBytes(body));

        var response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(SecurityProblemCodes.PayloadTooLarge, await CodeOf(response));
    }

    [Fact]
    public async Task An_unsigned_response_endpoint_is_signed_with_the_unsigned_body_marker()
    {
        using var request = SigningHost.Request(HttpMethod.Get, "/api/file");
        var nonce = Signer.Sign(request, _host.Clock.GetUtcNow());

        var response = await _host.Client.SendAsync(request);
        var mac = response.Headers.GetValues(TestProtocols.Test.ResponseSignatureHeaderName).Single()[2..];
        var fields = new SignedResponseFields(nonce, StatusCodes.Status200OK, "application/octet-stream", SignatureCanonicalForm.UnsignedBody);

        Assert.True(HmacSignature.Verify(Signer.SessionKey, new SignatureCanonicalForm(TestProtocols.Test).ResponseBytes(fields), mac));
    }

    private async Task AssertRefusedAsync(HttpRequestMessage request, string code)
    {
        var response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(code, await CodeOf(response));
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty(SecurityProblemWriter.CodeMember).GetString();
    }
}

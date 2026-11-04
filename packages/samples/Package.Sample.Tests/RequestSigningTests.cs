using System.Net;
using System.Text.Json;
using Codelab.Security.Problems;
using Package.Sample.Api.Hosting;
using Package.Sample.Tests.Support;
using Xunit;

namespace Package.Sample.Tests;

/// <summary>Exit criterion: request signing in Enforce mode, replay and body tamper, and signed responses.</summary>
public sealed class RequestSigningTests : IDisposable
{
    private const string Body = """{"title":"Signed record","contactEmail":"someone@example.test"}""";
    private readonly SampleHost _host = new(SampleKeys.Create());

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task An_unsigned_call_from_a_session_is_refused_401()
    {
        var session = _host.NewSession();

        var response = await session.SendUnsignedAsync(HttpMethod.Post, SampleRoutes.Records, Body);

        await AssertSecurityCodeAsync(response, SecurityProblemCodes.SignatureMissing);
    }

    [Fact]
    public async Task A_signed_call_is_served_and_its_response_signature_verifies()
    {
        var session = _host.NewSession();

        var exchange = await session.SendAsync(HttpMethod.Post, SampleRoutes.Records, Body);

        Assert.Equal(HttpStatusCode.OK, exchange.Response.StatusCode);
        Assert.True(await session.VerifiesAsync(exchange), "the response must carry a valid response signature");
        using var record = JsonDocument.Parse(await exchange.Response.Content.ReadAsStringAsync());
        Assert.Equal("Signed record", record.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_replayed_proof_is_refused_401()
    {
        var session = _host.NewSession();
        var first = await session.SendAsync(HttpMethod.Post, SampleRoutes.Records, Body);

        var replay = await session.SendAsync(HttpMethod.Post, SampleRoutes.Records, Body, proof: first.Proof);

        Assert.Equal(HttpStatusCode.OK, first.Response.StatusCode);
        await AssertSecurityCodeAsync(replay.Response, SecurityProblemCodes.SignatureReplayed);
    }

    [Fact]
    public async Task A_body_changed_after_signing_is_refused_401()
    {
        var session = _host.NewSession();

        var tampered = await session.SendAsync(HttpMethod.Post, SampleRoutes.Records, Body.Replace("Signed", "Forged", StringComparison.Ordinal), signedBody: Body);

        await AssertSecurityCodeAsync(tampered.Response, SecurityProblemCodes.SignatureInvalid);
    }

    [Fact]
    public async Task A_proof_from_another_session_is_refused_401()
    {
        var owner = _host.NewSession();
        var other = _host.NewSession();
        var stolen = await owner.SendAsync(HttpMethod.Post, SampleRoutes.Records, Body);

        var response = await other.SendAsync(HttpMethod.Post, SampleRoutes.Records, Body, proof: stolen.Proof);

        await AssertSecurityCodeAsync(response.Response, SecurityProblemCodes.SignatureInvalid);
    }

    private static async Task AssertSecurityCodeAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }
}

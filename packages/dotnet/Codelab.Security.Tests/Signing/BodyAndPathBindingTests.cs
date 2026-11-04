using System.Globalization;
using System.Net;
using System.Text;
using Codelab.Security.ServiceAuth;
using Codelab.Security.Signing;
using Codelab.Security.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Codelab.Security.Tests.Signing;

/// <summary>
/// What a proof is bound to, on real framing: an HTTP/2 body without Content-Length (no Transfer-Encoding either) is
/// hashed, never treated as empty; and a browser proof covers Request.Path without PathBase (IIS child application).
/// </summary>
[Collection(SharedCodecGroup.Name)]
public sealed class BodyAndPathBindingTests
{
    private const string EchoPath = "/api/echo";
    private const string ServiceEchoPath = "/api/internal/echo";
    private const string SignedBody = "{\"value\":1}";
    private const string TamperedBody = "{\"tampered\":true}";
    private const string Caller = "starter-worker";
    private const string ServiceKeyId = "s1";
    private const string ServiceAudience = "starter-api";
    private const string ChildApplicationBase = "/starter-api";

    private readonly string _master = TestKeys.NewKey();

    [Fact]
    public async Task An_http2_body_without_content_length_is_bound_to_the_request_signature()
    {
        await using var host = await SecurityTestHost.StartAsync(Http2(SigningHost.Options(SecurityEnforcementMode.Enforce, _master)));

        var signedAsEmpty = await SendSignedAsync(host, proofBody: [], sentBody: TamperedBody);
        var signedAsSent = await SendSignedAsync(host, proofBody: Encoding.UTF8.GetBytes(SignedBody), sentBody: SignedBody);

        Assert.Equal(HttpStatusCode.Unauthorized, signedAsEmpty.StatusCode);
        Assert.Equal(HttpVersion.Version20, signedAsEmpty.Version);
        Assert.Equal(HttpStatusCode.OK, signedAsSent.StatusCode);
        Assert.Equal(SignedBody, await signedAsSent.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_http2_body_without_content_length_is_bound_to_the_service_assertion()
    {
        var keys = TestKeys.NewEcKeyPair();
        await using var host = await SecurityTestHost.StartAsync(Http2(ServiceHost(keys.PublicPem)));
        using var issuer = new ServiceAssertionIssuer(
            new ServiceAssertionClientOptions { ServiceName = Caller, Audience = ServiceAudience, KeyId = ServiceKeyId, PrivateKeyPem = keys.PrivatePem }, host.Clock, TestProtocols.Test);
        var target = new Uri(host.Client.BaseAddress!, ServiceEchoPath);

        var assertedEmpty = await SendAssertedAsync(host, issuer.Issue(HttpMethod.Post, target, []), TamperedBody);
        var assertedAsSent = await SendAssertedAsync(host, issuer.Issue(HttpMethod.Post, target, Encoding.UTF8.GetBytes(SignedBody)), SignedBody);

        Assert.Equal(HttpStatusCode.Unauthorized, assertedEmpty.StatusCode);
        Assert.Equal(HttpStatusCode.OK, assertedAsSent.StatusCode);
        Assert.Equal(Caller + ":" + SignedBody, await assertedAsSent.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_browser_proof_covers_the_path_without_PathBase()
    {
        var options = SigningHost.Options(SecurityEnforcementMode.Enforce, _master);
        options.PathBase = ChildApplicationBase;
        await using var host = await SecurityTestHost.StartAsync(options);

        var serviceRelative = await SendMeAsync(host, signedPath: "/api/me");
        var withPathBase = await SendMeAsync(host, signedPath: ChildApplicationBase + "/api/me");

        Assert.Equal(HttpStatusCode.OK, serviceRelative.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, withPathBase.StatusCode);
    }

    private static TestHostOptions Http2(TestHostOptions options)
    {
        options.UseKestrelHttp2 = true;
        return options;
    }

    private static TestHostOptions ServiceHost(string publicKeyPem)
    {
        var options = SecurityTestHost.BaseOptions();
        var section = SecuritySections.ServiceAssertionServer;
        options.Settings[$"{section}:Audience"] = ServiceAudience;
        options.Settings[$"{section}:TrustedServices:0:ServiceName"] = Caller;
        options.Settings[$"{section}:TrustedServices:0:KeyId"] = ServiceKeyId;
        options.Settings[$"{section}:TrustedServices:0:PublicKeyPem"] = publicKeyPem;
        options.Settings[$"{SecuritySections.ReplayProtection}:AcknowledgeSingleInstance"] = bool.TrueString;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(options.Settings).Build();
        options.ConfigureServices = services => services.AddAuthentication().AddCodelabServiceAssertion(configuration);
        options.MapEndpoints = endpoints => endpoints
            .MapPost(ServiceEchoPath, ServiceEchoAsync)
            .RequireAuthorization(policy => policy.AddAuthenticationSchemes(ServiceAssertionAuthenticationHandler.SchemeName).RequireAuthenticatedUser());
        return options;
    }

    private static async Task<IResult> ServiceEchoAsync(HttpRequest request, System.Security.Claims.ClaimsPrincipal user)
    {
        using var reader = new StreamReader(request.Body);
        return TypedResults.Text(user.FindFirst(Codelab.Security.Sessions.SecurityClaimTypes.Service)!.Value + ":" + await reader.ReadToEndAsync());
    }

    private async Task<HttpResponseMessage> SendSignedAsync(SecurityTestHost host, byte[] proofBody, string sentBody)
    {
        using var request = SigningHost.Request(HttpMethod.Post, EchoPath);
        request.Content = new UnknownLengthContent(sentBody);
        AsHttp2(request);
        SigningHost.Signer(_master).Sign(request, host.Clock.GetUtcNow(), proofBody);
        return await host.Client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendAssertedAsync(SecurityTestHost host, string assertion, string sentBody)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ServiceEchoPath) { Content = new UnknownLengthContent(sentBody) };
        request.Headers.Add(TestProtocols.Test.ServiceAssertionHeaderName, assertion);
        AsHttp2(request);
        return await host.Client.SendAsync(request);
    }

    private static void AsHttp2(HttpRequestMessage request)
    {
        request.Version = HttpVersion.Version20;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
    }

    private async Task<HttpResponseMessage> SendMeAsync(SecurityTestHost host, string signedPath)
    {
        using var request = SigningHost.Request(HttpMethod.Get, ChildApplicationBase + "/api/me");
        var fields = new SignedRequestFields(
            Audience: SigningHost.Audience,
            Method: HttpMethod.Get.Method,
            Path: signedPath,
            Query: string.Empty,
            BodyHash: SignatureCanonicalForm.HashBody([]),
            Timestamp: host.Clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            Nonce: RequestSigner.NewNonce());
        request.Headers.Add(TestProtocols.Test.SignatureHeaderName, SigningHost.Signer(_master).Proof(fields));
        return await host.Client.SendAsync(request);
    }

    /// <summary>A body whose length is not known up front: HTTP/2 sends it with no Content-Length and no Transfer-Encoding.</summary>
    private sealed class UnknownLengthContent(string text) : HttpContent
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(text);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(_bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}

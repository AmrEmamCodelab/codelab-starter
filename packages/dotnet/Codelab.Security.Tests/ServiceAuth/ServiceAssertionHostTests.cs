using System.Net;
using System.Text;
using Codelab.Security.ServiceAuth;
using Codelab.Security.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;

namespace Codelab.Security.Tests.ServiceAuth;

/// <summary>The scheme end to end: the client handler signs, the server scheme authenticates, a replay is refused 401.</summary>
[Collection(SharedCodecGroup.Name)]
public sealed class ServiceAssertionHostTests : IAsyncLifetime
{
    private const string Caller = "attachment-service";
    private readonly (string PrivatePem, string PublicPem) _keys = TestKeys.NewEcKeyPair();
    private SecurityTestHost _host = null!;

    public async Task InitializeAsync()
    {
        var options = SecurityTestHost.BaseOptions();
        options.Settings[$"{SecuritySections.ServiceAssertionServer}:Audience"] = "starter-api";
        options.Settings[$"{SecuritySections.ServiceAssertionServer}:TrustedServices:0:ServiceName"] = Caller;
        options.Settings[$"{SecuritySections.ServiceAssertionServer}:TrustedServices:0:KeyId"] = "s1";
        options.Settings[$"{SecuritySections.ServiceAssertionServer}:TrustedServices:0:PublicKeyPem"] = _keys.PublicPem;
        options.ConfigureServices = services => services.AddAuthentication().AddCodelabServiceAssertion(Configuration(options.Settings));
        options.MapEndpoints = endpoints => endpoints
            .MapPost("/api/internal/echo", Echo)
            .RequireAuthorization(policy => policy.AddAuthenticationSchemes(ServiceAssertionAuthenticationHandler.SchemeName).RequireAuthenticatedUser());
        _host = await SecurityTestHost.StartAsync(options);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_signed_service_call_is_authenticated_as_the_calling_service()
    {
        using var client = SignedClient();

        var response = await client.PostAsync("/api/internal/echo", new StringContent("{\"a\":1}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Caller + ":{\"a\":1}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_replayed_assertion_is_refused_401()
    {
        using var issuer = Issuer();
        var assertion = issuer.Issue(HttpMethod.Post, new Uri(_host.Client.BaseAddress!, "/api/internal/echo"), "{}"u8);

        var first = await SendRawAsync(assertion);
        var replay = await SendRawAsync(assertion);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task A_call_without_an_assertion_or_with_a_tampered_body_is_refused_401()
    {
        using var issuer = Issuer();
        var assertion = issuer.Issue(HttpMethod.Post, new Uri(_host.Client.BaseAddress!, "/api/internal/echo"), "{}"u8);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client.PostAsync("/api/internal/echo", new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendRawAsync(assertion, body: "{\"tampered\":true}")).StatusCode);
    }

    private static ContentHttpResult Echo(HttpContext context, [Microsoft.AspNetCore.Mvc.FromBody] System.Text.Json.JsonElement body) =>
        TypedResults.Text(context.User.FindFirst(Codelab.Security.Sessions.SecurityClaimTypes.Service)!.Value + ":" + body.GetRawText());

    private async Task<HttpResponseMessage> SendRawAsync(string assertion, string body = "{}")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/echo") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add(TestProtocols.Test.ServiceAssertionHeaderName, assertion);
        return await _host.Client.SendAsync(request);
    }

    private ServiceAssertionIssuer Issuer() =>
        new(new ServiceAssertionClientOptions { ServiceName = Caller, Audience = "starter-api", KeyId = "s1", PrivateKeyPem = _keys.PrivatePem }, _host.Clock, TestProtocols.Test);

    private HttpClient SignedClient()
    {
        var handler = new ServiceAssertionHandler(Issuer(), TestProtocols.Test) { InnerHandler = _host.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>() is Microsoft.AspNetCore.TestHost.TestServer server ? server.CreateHandler() : throw new InvalidOperationException() };
        return new HttpClient(handler) { BaseAddress = _host.Client.BaseAddress };
    }

    private static IConfiguration Configuration(IDictionary<string, string?> settings) => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}

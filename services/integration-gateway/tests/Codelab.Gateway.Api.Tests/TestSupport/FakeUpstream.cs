using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Codelab.Gateway.Api.Tests.TestSupport;
// codelab:module-begin Sms

/// <summary>One SMS the stub provider received.</summary>
internal sealed record RecordedSms(string Method, string PathAndQuery, string? Credential, string? ContentType, string Body);
// codelab:module-end Sms

/// <summary>
/// The upstreams the real gateway cannot reach here, played by one loopback Kestrel host over HTTPS with a certificate
/// generated for this run: an OIDC provider (token endpoint and JWKS) and an SMS provider stub that records every
/// message. Each answer's status and body can be changed per test; everything received is recorded.
/// </summary>
internal sealed class FakeUpstream : IAsyncDisposable
{
    public const string TokenPath = "/oidc/token";
    public const string JwksPath = "/oidc/jwks";
    public const string SmsPath = "/sms/send"; // codelab:module Sms
    public const string RedirectPath = "/oidc/moved";
    public const string CredentialHeader = "X-Provider-Key"; // codelab:module Sms
    public const string TokenJson = """{"access_token":"access-1","id_token":"identity-1","token_type":"Bearer","expires_in":300}""";
    public const string JwksJson = """{"keys":[{"kty":"EC","kid":"k1","crv":"P-256","x":"AQ","y":"AQ"}]}""";
    public const string SmsJson = """{"ok":true,"result":{"id":"msg-1"}}"""; // codelab:module Sms

    private readonly WebApplication _app;
    private int _jwksHits;

    private FakeUpstream(WebApplication app, X509Certificate2 certificate)
    {
        _app = app;
        Certificate = certificate;
    }

    public X509Certificate2 Certificate { get; }

    public string Thumbprint => TestKeys.Sha256Thumbprint(Certificate);

    public Uri BaseAddress { get; private set; } = null!;

    public int TokenStatus { get; set; } = StatusCodes.Status200OK;

    public string TokenBody { get; set; } = TokenJson;

    public int JwksStatus { get; set; } = StatusCodes.Status200OK;

    public string JwksBody { get; set; } = JwksJson;
    // codelab:module-begin Sms

    public int SmsStatus { get; set; } = StatusCodes.Status200OK;

    public string SmsBody { get; set; } = SmsJson;
    // codelab:module-end Sms

    public int JwksHits => Volatile.Read(ref _jwksHits);

    public ConcurrentQueue<string> TokenForms { get; } = new();
    // codelab:module-begin Sms

    public ConcurrentQueue<RecordedSms> Messages { get; } = new();
    // codelab:module-end Sms

    public static async Task<FakeUpstream> StartAsync()
    {
        var certificate = TestKeys.NewLoopbackCertificate();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
        var upstream = new FakeUpstream(builder.Build(), certificate);
        upstream.MapRoutes();
        await upstream._app.StartAsync();
        var address = upstream._app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        upstream.BaseAddress = new Uri(address.Replace("0.0.0.0", "127.0.0.1", StringComparison.Ordinal));
        return upstream;
    }

    public Uri Url(string pathAndQuery) => new(BaseAddress, pathAndQuery);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        Certificate.Dispose();
    }

    private void MapRoutes()
    {
        _app.MapPost(TokenPath, async (HttpContext context) =>
        {
            TokenForms.Enqueue(await ReadBodyAsync(context.Request));
            return Results.Text(TokenBody, "application/json", statusCode: TokenStatus);
        });
        _app.MapGet(JwksPath, () =>
        {
            Interlocked.Increment(ref _jwksHits);
            return Results.Text(JwksBody, "application/json", statusCode: JwksStatus);
        });
        _app.MapGet(RedirectPath, () => Results.Redirect(JwksPath));
        // codelab:module-begin Sms
        _app.MapMethods(SmsPath, [HttpMethods.Get, HttpMethods.Post], async (HttpContext context) =>
        {
            var request = context.Request;
            Messages.Enqueue(new RecordedSms(request.Method, request.Path + request.QueryString, request.Headers[CredentialHeader].FirstOrDefault(), request.ContentType, await ReadBodyAsync(request)));
            return Results.Text(SmsBody, "application/json", statusCode: SmsStatus);
        });
        // codelab:module-end Sms
    }

    private static async Task<string> ReadBodyAsync(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body);
        return await reader.ReadToEndAsync();
    }
}

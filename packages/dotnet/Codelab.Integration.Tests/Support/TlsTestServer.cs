using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Codelab.Integration.Tests.Support;

/// <summary>An in-process Kestrel server on a free loopback port, serving HTTPS with the given certificate.</summary>
internal sealed class TlsTestServer : IAsyncDisposable
{
    public const string PingPath = "/ping";
    public const string PingBody = "pong";

    private readonly WebApplication _app;

    private TlsTestServer(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    public static async Task<TlsTestServer> StartAsync(X509Certificate2 serverCertificate)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrelHttpsConfiguration();
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(IPAddress.Loopback, port: 0, listen => listen.UseHttps(serverCertificate)));
        var app = builder.Build();
        app.MapGet(PingPath, () => PingBody);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new TlsTestServer(app, new Uri(address));
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

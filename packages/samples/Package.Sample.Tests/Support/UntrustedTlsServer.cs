using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Package.Sample.Tests.Support;

/// <summary>
/// An in-process Kestrel HTTPS server whose certificate is self-signed at run time for <c>localhost</c>: the host
/// name matches, only the chain is untrusted. It counts the requests that reach it; a refused handshake reaches none.
/// </summary>
internal sealed class UntrustedTlsServer : IDisposable
{
    private const string HostName = "localhost";
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
    private readonly WebApplication _app;
    private readonly X509Certificate2 _certificate;
    private int _requests;

    private UntrustedTlsServer(WebApplication app, X509Certificate2 certificate)
    {
        _app = app;
        _certificate = certificate;
    }

    public Uri BaseAddress { get; private set; } = null!;

    public int Requests => Volatile.Read(ref _requests);

    public static UntrustedTlsServer Start()
    {
        var certificate = SelfSignedCertificate();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
        var app = builder.Build();
        var server = new UntrustedTlsServer(app, certificate);
        app.Run(server.CountAsync);
        app.StartAsync().GetAwaiter().GetResult();
        var bound = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        server.BaseAddress = new UriBuilder(bound) { Host = HostName }.Uri;
        return server;
    }

    public void Dispose()
    {
        _app.StopAsync().GetAwaiter().GetResult();
        ((IDisposable)_app).Dispose();
        _certificate.Dispose();
    }

    private Task CountAsync(HttpContext context)
    {
        Interlocked.Increment(ref _requests);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return Task.CompletedTask;
    }

    // Schannel cannot serve an ephemeral key, so the certificate is round-tripped through PKCS#12.
    private static X509Certificate2 SelfSignedCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=" + HostName, key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(HostName);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuthenticationOid)], critical: false));
        var now = TimeProvider.System.GetUtcNow();
        using var ephemeral = request.CreateSelfSigned(now.AddMinutes(-5), now.AddHours(1));
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), password: null);
    }
}

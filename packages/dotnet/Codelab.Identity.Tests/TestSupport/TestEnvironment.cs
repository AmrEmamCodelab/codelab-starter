using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Codelab.Identity.Tests.TestSupport;

/// <summary>A host environment by name, for the options validators.</summary>
internal sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;

    public string ApplicationName { get; set; } = "Codelab.Identity.Tests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

/// <summary>
/// A loopback TLS listener with a self-signed certificate generated at run time (untrusted on purpose), or a plain TCP
/// listener. Accepts connections and serves the handshake; nothing else.
/// </summary>
internal sealed class LoopbackListener : IAsyncDisposable
{
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    private readonly TcpListener listener = new(IPAddress.Loopback, port: 0);
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;

    private LoopbackListener(X509Certificate2? certificate)
    {
        Certificate = certificate;
        listener.Start();
        loop = AcceptAsync();
    }

    public X509Certificate2? Certificate { get; }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public static LoopbackListener Tls(TimeProvider clock) => new(SelfSigned(clock));

    public static LoopbackListener Plain() => new(certificate: null);

    /// <summary>A port nothing listens on (bound, read, released).</summary>
    public static int ClosedPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, port: 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        listener.Stop();
        try
        {
            await loop;
        }
        catch (Exception failure) when (failure is OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }

        Certificate?.Dispose();
        stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(stop.Token);
            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            if (Certificate is null)
            {
                return;
            }

            try
            {
                await using var tls = new System.Net.Security.SslStream(client.GetStream());
                await tls.AuthenticateAsServerAsync(Certificate);
            }
            catch (Exception failure) when (failure is IOException or System.Security.Authentication.AuthenticationException)
            {
                // The client refused the untrusted certificate: expected.
            }
        }
    }

    private static X509Certificate2 SelfSigned(TimeProvider clock)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=directory-test-server", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuthenticationOid)], critical: false));
        var now = clock.GetUtcNow();
        using var created = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(1));

        // Schannel cannot serve TLS with an ephemeral key; a PKCS#12 round trip gives the key a container.
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12), password: null);
    }
}

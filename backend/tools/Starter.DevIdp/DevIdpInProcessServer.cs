using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;
using Starter.DevIdp.Configuration;
using Starter.DevIdp.Hosting;

namespace Starter.DevIdp;

/// <summary>
/// The dev IdP hosted in process on TestServer, for tests (no socket, no port). Settings start from the embedded
/// appsettings.Development.json (the starter-web client and the fixture users) and nothing else: environment
/// variables and the test project's own appsettings files are NOT read. <c>configure</c> adjusts them (add a client
/// secret, register a test redirect URI, clear and replace the users).
/// <para>
/// Give the API's named OIDC HttpClient <see cref="CreateHandler"/> as its primary handler; requests to any URL
/// reach this IdP, and every URL it publishes (discovery, redirects, iss) is built on <see cref="Issuer"/>.
/// A test plays the browser with <see cref="CreateClient"/> (redirects are NOT followed, so each 302 can be checked).
/// </para>
/// </summary>
public sealed class DevIdpInProcessServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly TestServer _server;

    private DevIdpInProcessServer(WebApplication app)
    {
        _app = app;
        _server = app.GetTestServer();
        Options = app.Services.GetRequiredService<IOptions<DevIdpOptions>>().Value;
        Issuer = new Uri(Options.Issuer);
        _server.BaseAddress = Issuer;
    }

    /// <summary>The effective settings (after <c>configure</c>).</summary>
    public DevIdpOptions Options { get; }

    /// <summary>The issuer every published URL is built on.</summary>
    public Uri Issuer { get; }

    /// <summary>The IdP's services (tests only).</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>
    /// Starts the IdP. <paramref name="configure"/> runs after the embedded settings are bound and before they are
    /// validated; <paramref name="timeProvider"/> replaces the system clock (code and token expiry, iat/exp).
    /// </summary>
    public static async Task<DevIdpInProcessServer> StartAsync(
        Action<DevIdpOptions>? configure = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = AppContext.BaseDirectory,
            ApplicationName = typeof(DevIdpApp).Assembly.GetName().Name,
        });
        builder.Configuration.Sources.Clear();
        DevIdpEmbeddedSettings.AddAsDefaults(builder.Configuration);
        builder.WebHost.UseTestServer();
        if (timeProvider is not null)
        {
            builder.Services.AddSingleton(timeProvider);
        }

        builder.Services.AddDevIdp(builder.Configuration);
        if (configure is not null)
        {
            builder.Services.PostConfigure(configure);
        }

        var app = builder.Build();
        app.MapDevIdp();
        try
        {
            await app.StartAsync(cancellationToken);
        }
        catch
        {
            // Invalid settings fail the start (ValidateOnStart); release the host before the failure propagates.
            await app.DisposeAsync();
            throw;
        }

        return new DevIdpInProcessServer(app);
    }

    /// <summary>A handler that sends requests to this IdP (for the API's named OIDC HttpClient).</summary>
    public HttpMessageHandler CreateHandler() => _server.CreateHandler();

    /// <summary>A client on <see cref="Issuer"/> that does not follow redirects (the test plays the browser).</summary>
    public HttpClient CreateClient()
    {
        var client = _server.CreateClient();
        client.BaseAddress = Issuer;
        return client;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

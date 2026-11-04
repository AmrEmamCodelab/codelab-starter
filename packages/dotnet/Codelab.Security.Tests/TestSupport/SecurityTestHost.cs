using System.Net;
using Codelab.Security.Keys;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Codelab.Security.Tests.TestSupport;

/// <summary>What a test host is built from.</summary>
internal sealed class TestHostOptions
{
    public Dictionary<string, string?> Settings { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string EnvironmentName { get; init; } = Environments.Production;

    public FakeTimeProvider Clock { get; init; } = new(new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero));

    public Action<IServiceCollection>? ConfigureServices { get; set; }

    public Action<IEndpointRouteBuilder>? MapEndpoints { get; set; }

    public bool UseMvc { get; set; }

    public PublishedMaterialDenylist? Denylist { get; set; }

    /// <summary>When set, the app runs under this PathBase (an IIS child application), applied with UsePathBase.</summary>
    public string? PathBase { get; set; }

    /// <summary>Run on real Kestrel over cleartext HTTP/2 (h2c) instead of TestServer, to see HTTP/2 framing.</summary>
    public bool UseKestrelHttp2 { get; set; }

    /// <summary>
    /// The default authentication scheme; null registers the test scheme with NO default, so only an endpoint policy
    /// that names the scheme authenticates the user (at authorization time, after UseCodelabSecurity).
    /// </summary>
    public string? DefaultScheme { get; set; } = TestAuthentication.SchemeName;
}

/// <summary>An in-process host (TestServer, or Kestrel over h2c when asked) running AddCodelabSecurity/UseCodelabSecurity behind test authentication.</summary>
internal sealed class SecurityTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private SecurityTestHost(WebApplication app, FakeTimeProvider clock, HttpClient client)
    {
        _app = app;
        Clock = clock;
        Client = client;
    }

    public HttpClient Client { get; }

    public FakeTimeProvider Clock { get; }

    public IServiceProvider Services => _app.Services;

    /// <summary>
    /// The base settings every host needs: the test prefix, a fresh PublicId ring and the single-instance
    /// acknowledgement for the memory stores (a test host is one process; the guard has its own tests).
    /// </summary>
    public static TestHostOptions BaseOptions(string environment = "Production")
    {
        var options = new TestHostOptions { EnvironmentName = environment };
        options.Settings[$"{SecuritySections.Protocol}:Prefix"] = SecurityProtocol.TestPrefix;
        options.Settings[$"{SecuritySections.ReplayProtection}:AcknowledgeSingleInstance"] = bool.TrueString;
        TestSettings.AddRing(options.Settings, SecuritySections.PublicIds, "k1");
        return options;
    }

    public static async Task<SecurityTestHost> StartAsync(TestHostOptions options)
    {
        var app = Build(options);
        await app.StartAsync();
        return new SecurityTestHost(app, options.Clock, options.UseKestrelHttp2 ? Http2Client(app) : app.GetTestClient());
    }

    public static WebApplication Build(TestHostOptions options)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = options.EnvironmentName });
        UseServer(builder, options.UseKestrelHttp2);
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(options.Settings);
        builder.Services.AddSingleton<TimeProvider>(options.Clock);
        if (options.Denylist is not null)
        {
            builder.Services.AddSingleton(options.Denylist);
        }

        AddTestAuthentication(builder.Services, options.DefaultScheme);
        builder.Services.AddAuthorization();
        builder.AddCodelabSecurity();
        AddMvcIfRequested(builder.Services, options.UseMvc);
        options.ConfigureServices?.Invoke(builder.Services);
        var app = builder.Build();
        if (options.PathBase is not null)
        {
            app.UsePathBase(options.PathBase);
        }

        app.Use(TestConnection.ApplyRemoteAddress);
        app.Use(Edge.LimitHeader.Apply);
        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseCodelabSecurity();
        app.UseAuthorization();
        options.MapEndpoints?.Invoke(app);
        MapControllersIfRequested(app, options.UseMvc);
        return app;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static void UseServer(WebApplicationBuilder builder, bool kestrelHttp2)
    {
        if (!kestrelHttp2)
        {
            builder.WebHost.UseTestServer();
            return;
        }

        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, port: 0, listen => listen.Protocols = HttpProtocols.Http2));
    }

    private static HttpClient Http2Client(WebApplication app)
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new HttpClient
        {
            BaseAddress = new Uri(address),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
    }

    private static void AddTestAuthentication(IServiceCollection services, string? defaultScheme)
    {
        if (defaultScheme is not null)
        {
            services.AddAuthentication(defaultScheme).AddScheme<AuthenticationSchemeOptions, TestAuthentication>(TestAuthentication.SchemeName, configureOptions: null);
            return;
        }

        // A second scheme stops ASP.NET Core from inferring the only registered scheme as the default.
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, TestAuthentication>(TestAuthentication.SchemeName, configureOptions: null)
            .AddScheme<AuthenticationSchemeOptions, TestAuthentication>(TestAuthentication.SecondSchemeName, configureOptions: null);
    }

    private static void AddMvcIfRequested(IServiceCollection services, bool useMvc)
    {
        if (useMvc)
        {
            services.AddControllers().AddApplicationPart(typeof(SecurityTestHost).Assembly);
        }
    }

    private static void MapControllersIfRequested(WebApplication app, bool useMvc)
    {
        if (useMvc)
        {
            app.MapControllers();
        }
    }
}

/// <summary>Lets a test choose the connection address (TestServer has none).</summary>
internal static class TestConnection
{
    public const string RemoteAddressHeader = "Test-Remote-Address";

    public static async Task ApplyRemoteAddress(HttpContext context, RequestDelegate next)
    {
        if (IPAddress.TryParse(context.Request.Headers[RemoteAddressHeader].ToString(), out var address))
        {
            context.Connection.RemoteIpAddress = address;
        }

        await next(context);
    }
}

/// <summary>Configuration helpers.</summary>
internal static class TestSettings
{
    public static void AddRing(IDictionary<string, string?> settings, string section, params string[] keyIds)
    {
        for (var index = 0; index < keyIds.Length; index++)
        {
            settings[$"{section}:Keys:{index}:KeyId"] = keyIds[index];
            settings[$"{section}:Keys:{index}:MaterialBase64"] = TestKeys.NewKey();
        }

        settings[$"{section}:ActiveKeyId"] = keyIds[0];
    }

    public static void AddMaterial(IDictionary<string, string?> settings, string section, string keyId, string material)
    {
        settings[$"{section}:Keys:0:KeyId"] = keyId;
        settings[$"{section}:Keys:0:MaterialBase64"] = material;
        settings[$"{section}:ActiveKeyId"] = keyId;
    }

    public static IServiceCollection Replace<TService>(this IServiceCollection services, TService instance)
        where TService : class
    {
        services.RemoveAll<TService>();
        services.AddSingleton(instance);
        return services;
    }
}

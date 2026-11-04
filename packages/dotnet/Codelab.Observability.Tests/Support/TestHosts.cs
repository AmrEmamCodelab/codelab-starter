using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog.Core;

namespace Codelab.Observability.Tests.Support;

/// <summary>What a test host is built from.</summary>
/// <param name="Settings">Configuration values added on top of the defaults (file sink off, a fresh hash key).</param>
/// <param name="ConfigureServices">Extra service registrations.</param>
/// <param name="ConfigureApp">The pipeline and endpoints.</param>
/// <param name="EnvironmentName">The ASP.NET Core environment.</param>
public sealed record TestHostSpec(
    IReadOnlyDictionary<string, string?> Settings,
    Action<IServiceCollection>? ConfigureServices,
    Action<WebApplication> ConfigureApp,
    string EnvironmentName);

/// <summary>Builds in-process hosts (TestServer) wired with <c>AddCodelabObservability</c> and a capture sink.</summary>
public static class TestHosts
{
    public const string ServiceName = "starter-test";
    private const int HashKeyBytes = 32;

    /// <summary>A fresh base64 key generated for this run; no key is ever committed.</summary>
    public static string NewHashKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(HashKeyBytes));

    /// <summary>The defaults every test host starts from: no log file, a fresh user-id hash key.</summary>
    public static Dictionary<string, string?> DefaultSettings() => new()
    {
        ["Observability:LogFile:Enabled"] = "false",
        ["Observability:UserIdHash:Key"] = NewHashKey(),
    };

    /// <summary>Builds and starts a host for <paramref name="spec"/>; the capture sink receives every retained event.</summary>
    public static async Task<(WebApplication App, CaptureSink Sink)> StartAsync(TestHostSpec spec)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = spec.EnvironmentName });
        builder.WebHost.UseTestServer();
        var settings = DefaultSettings();
        foreach (var (key, value) in spec.Settings)
        {
            settings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(settings);
        var sink = new CaptureSink();
        builder.Services.AddSingleton<ILogEventSink>(sink);
        spec.ConfigureServices?.Invoke(builder.Services);
        builder.AddCodelabObservability(new ObservabilityOptions
        {
            ServiceName = ServiceName,
            HostAssembly = typeof(TestHosts).Assembly,
            IsUnsafeForRetainedSinks = UnsafeEvents.IsUnsafe,
        });

        var app = builder.Build();
        spec.ConfigureApp(app);
        await app.StartAsync();
        return (app, sink);
    }

    /// <summary>A spec with only an app configuration, in Production.</summary>
    public static TestHostSpec Pipeline(Action<WebApplication> configureApp) =>
        new(new Dictionary<string, string?>(), ConfigureServices: null, configureApp, Environments.Production);
}

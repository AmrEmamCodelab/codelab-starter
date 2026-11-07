using System.Collections.Concurrent;
using System.Security.Cryptography;
using Codelab.Errors;
using Codelab.Observability;
using Codelab.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using Starter.Infrastructure.Errors;
using Starter.Infrastructure.Hosting;
using Starter.Infrastructure.Modularity;

namespace Starter.Api.Tests.SecurityEdge;

/// <summary>
/// The kernel host composed exactly like Program.cs (as <c>KernelTestHost</c>), with a capturing Serilog sink
/// registered in DI (Codelab.Observability reads DI sinks into the retained pipeline, the same place Seq sits) and an
/// environment of the test's choosing.
/// </summary>
internal static class SecurityEdgeHost
{
    public const string AllowedOrigin = "http://localhost:4280";
    private const int KeyBytes = 32;

    public static WebApplication Build(CapturingSink sink, IReadOnlyDictionary<string, string?>? overrides = null, string environment = "Development")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            ApplicationName = typeof(ModuleCatalog).Assembly.GetName().Name,
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(Settings(overrides));
        builder.Services.AddSingleton<ILogEventSink>(sink);
        builder.AddCodelabObservability("starter-api-tests");
        builder.Services.AddStarterKernel(builder.Configuration);
        builder.Services.AddCodelabErrors(errors => errors.AddStarterFailures());
        builder.AddCodelabSecurity();
        builder.Services.AddStarterModules(ModuleCatalog.Empty, builder.Configuration);

        var app = builder.Build();
        try
        {
            app.UseCodelabObservability();
            app.UseCodelabErrors();
            app.UseStarterPipeline();
            app.MapStarterEndpoints(ModuleCatalog.Empty);
            return app;
        }
        catch
        {
            // A boot test expects the pipeline to throw; release the built host before the exception reaches it.
            ((IDisposable)app).Dispose();
            throw;
        }
    }

    public static async Task<WebApplication> StartAsync(CapturingSink sink, IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var app = Build(sink, overrides);
        await app.StartAsync();
        return app;
    }

    private static Dictionary<string, string?> Settings(IReadOnlyDictionary<string, string?>? overrides)
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ConnectionStrings:Starter"] = "Server=(localdb)\\unused;Database=never-opened",
            ["Database:ApplyMigrationsOnStartup"] = "false",
            ["Codelab:Security:Protocol:Prefix"] = "STARTER",
            ["Codelab:Security:PublicIds:ActiveKeyId"] = "t1",
            ["Codelab:Security:PublicIds:Keys:0:KeyId"] = "t1",
            ["Codelab:Security:PublicIds:Keys:0:MaterialBase64"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes)),
            ["Codelab:Security:ReplayProtection:Store"] = "Memory",
            ["Codelab:Security:Sessions:Store"] = "Memory",
            ["Codelab:Security:Cors:AllowedOrigins:0"] = AllowedOrigin,
            ["Observability:LogFile:Enabled"] = "false",
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        return settings;
    }
}

/// <summary>A Serilog sink that keeps every event it receives.</summary>
internal sealed class CapturingSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyList<LogEvent> Events => [.. _events];

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
}

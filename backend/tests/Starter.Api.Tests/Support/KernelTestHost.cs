using System.Security.Cryptography;
using Codelab.Errors;
using Codelab.Observability;
using Codelab.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Starter.Infrastructure.Errors;
using Starter.Infrastructure.Hosting;
using Starter.Infrastructure.Modularity;

namespace Starter.Api.Tests.Support;

/// <summary>Tests that start hosts share the process-wide PublicId codec, so they run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HostTestGroup
{
    public const string Name = "kernel-host";
}

/// <summary>
/// An in-process host composed exactly like Program.cs (observability, kernel, errors, security, modules, pipeline,
/// endpoints) on TestServer, with in-memory security stores and no database: the connection string is never opened
/// because migrations are off and no probe module has a startup task.
/// </summary>
public static class KernelTestHost
{
    public const string AllowedOrigin = "http://localhost:4280";
    private const int KeyBytes = 32;

    public static async Task<WebApplication> StartAsync(ModuleCatalog catalog, IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ApplicationName = typeof(ModuleCatalog).Assembly.GetName().Name,
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(Settings(overrides));
        builder.AddCodelabObservability("starter-api-tests");
        builder.Services.AddStarterKernel(builder.Configuration);
        builder.Services.AddCodelabErrors(errors => errors.AddStarterFailures());
        builder.AddCodelabSecurity();
        builder.Services.AddStarterModules(catalog, builder.Configuration);

        var app = builder.Build();
        app.UseCodelabObservability();
        app.UseCodelabErrors();
        app.UseStarterPipeline();
        app.MapStarterEndpoints(catalog);
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

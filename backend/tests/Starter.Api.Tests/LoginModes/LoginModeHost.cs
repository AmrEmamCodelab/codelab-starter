using System.Security.Cryptography;
using Codelab.Errors;
using Codelab.Identity.ActiveDirectory;
using Codelab.Identity.Oidc;
using Codelab.Observability;
using Codelab.Security;
using Codelab.Security.Keys;
using Codelab.Security.Tickets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Starter.Application.Authorization;
using Starter.Infrastructure.Errors;
using Starter.Infrastructure.Hosting;
using Starter.Infrastructure.Modularity;
using Starter.Infrastructure.Startup;
using Starter.Modules.Identity.Infrastructure;
using Starter.Modules.Identity.Infrastructure.LoginModes;
using Starter.Modules.Identity.Infrastructure.Options;

namespace Starter.Api.Tests.LoginModes;

/// <summary>
/// The Identity module in a host composed exactly like Program.cs (observability, kernel, errors, security, modules,
/// pipeline, endpoints) on TestServer in Development, without a database: in-memory security stores, no migrations and
/// no startup tasks (they seed through SQL). Only <c>Auth:Modes</c> and the providers' sections change between runs.
/// </summary>
public static class LoginModeHost
{
    /// <summary>The SPA base URL the SSO runs use.</summary>
    public const string WebBaseUrl = "https://portal.example.test";

    /// <summary>The identity provider the SSO runs point at (never contacted at start).</summary>
    public const string IdentityProvider = "https://idp.example.test";

    private const int KeyBytes = 32;
    private const string KeyId = "t1";
    private const string FirstItem = "0";

    /// <summary>Starts the host with <paramref name="settings"/> on top of the base settings.</summary>
    public static async Task<WebApplication> StartAsync(IReadOnlyDictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ApplicationName = typeof(ModuleCatalog).Assembly.GetName().Name,
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(BaseSettings().Concat(settings));
        var catalog = ModuleCatalog.FromModules([new IdentityModule()]);
        builder.AddCodelabObservability("starter-api-tests");
        builder.Services.AddStarterKernel(builder.Configuration);
        builder.Services.AddCodelabErrors(errors => errors.AddStarterFailures());
        builder.AddCodelabSecurity();
        builder.Services.AddStarterModules(catalog, builder.Configuration);
        builder.Services.AddSingleton<IUserPermissionGrants, NoPermissionGrants>();
        RemoveStartupTasks(builder.Services);

        var app = builder.Build();
        app.UseCodelabObservability();
        app.UseCodelabErrors();
        app.UseStarterPipeline();
        app.MapStarterEndpoints(catalog);
        await app.StartAsync();
        return app;
    }

    /// <summary>A coherent Active Directory section (never contacted at start).</summary>
    public static Dictionary<string, string?> DirectorySection() => new(StringComparer.OrdinalIgnoreCase)
    {
        [Key(ActiveDirectoryOptions.SectionName, nameof(ActiveDirectoryOptions.Host))] = "dc.example.test",
        [Key(ActiveDirectoryOptions.SectionName, nameof(ActiveDirectoryOptions.Domain))] = "example.test",
        [Key(ActiveDirectoryOptions.SectionName, nameof(ActiveDirectoryOptions.BaseDn))] = "DC=example,DC=test",
    };

    /// <summary>A coherent SSO section, the SPA base URL and a sealed-ticket ring.</summary>
    public static Dictionary<string, string?> SsoSection() => new(StringComparer.OrdinalIgnoreCase)
    {
        [Key(OidcOptions.SectionName, nameof(OidcOptions.Authority))] = IdentityProvider,
        [Key(OidcOptions.SectionName, nameof(OidcOptions.ClientId))] = "starter-web",
        [Key(OidcOptions.SectionName, nameof(OidcOptions.RedirectUri))] = "https://api.example.test/api/auth/sso/signin-oidc",
        [Key(OidcOptions.SectionName, nameof(OidcOptions.PostLogoutRedirectUri))] = WebBaseUrl + "/login",
        [Key(OidcOptions.SectionName, nameof(OidcOptions.AuthorizationEndpoint))] = IdentityProvider + "/authorize",
        [Key(OidcOptions.SectionName, nameof(OidcOptions.TokenEndpoint))] = IdentityProvider + "/token",
        [Key(OidcOptions.SectionName, nameof(OidcOptions.EndSessionEndpoint))] = IdentityProvider + "/end-session",
        [Key(OidcOptions.SectionName, nameof(OidcOptions.JwksUri))] = IdentityProvider + "/jwks",
        [Key(AuthModesOptions.SectionName, nameof(AuthModesOptions.WebBaseUrl))] = WebBaseUrl,
        [Key(SealedTicketOptions.SectionName, nameof(SealedTicketOptions.Enabled))] = bool.TrueString,
        [Key(SealedTicketOptions.SectionName, nameof(KeyRingOptions.ActiveKeyId))] = KeyId,
        [Key(SealedTicketOptions.SectionName, nameof(KeyRingOptions.Keys), FirstItem, nameof(KeyEntryOptions.KeyId))] = KeyId,
        [Key(SealedTicketOptions.SectionName, nameof(KeyRingOptions.Keys), FirstItem, nameof(KeyEntryOptions.MaterialBase64))] = NewKey(),
    };

    /// <summary>The <c>Auth:Modes</c> settings for <paramref name="modes"/>.</summary>
    public static Dictionary<string, string?> Modes(params string[] modes) =>
        modes.Select((mode, index) => (Key: Key(AuthModesOptions.SectionName, nameof(AuthModesOptions.Modes), index.ToString(System.Globalization.CultureInfo.InvariantCulture)), Value: mode))
            .ToDictionary(item => item.Key, item => (string?)item.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>The union of <paramref name="parts"/> (later parts win).</summary>
    public static Dictionary<string, string?> Merge(params Dictionary<string, string?>[] parts)
    {
        var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parts.SelectMany(part => part))
        {
            merged[key] = value;
        }

        return merged;
    }

    private static string Key(params string[] segments) => ConfigurationPath.Combine(segments);

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes));

    private static Dictionary<string, string?> BaseSettings() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["ConnectionStrings:Starter"] = "Server=(localdb)\\unused;Database=never-opened",
        ["Database:ApplyMigrationsOnStartup"] = "false",
        ["Codelab:Security:Protocol:Prefix"] = "STARTER",
        ["Codelab:Security:PublicIds:ActiveKeyId"] = KeyId,
        ["Codelab:Security:PublicIds:Keys:0:KeyId"] = KeyId,
        ["Codelab:Security:PublicIds:Keys:0:MaterialBase64"] = NewKey(),
        ["Codelab:Security:ReplayProtection:Store"] = "Memory",
        ["Codelab:Security:Sessions:Store"] = "Memory",
        ["Codelab:Security:Cors:AllowedOrigins:0"] = "http://localhost:4280",
        ["Observability:LogFile:Enabled"] = "false",
        [UserIdHashSettings.KeyName] = NewKey(),
        [Key(JwtOptions.SectionName, nameof(JwtOptions.SigningKey))] = NewKey(),
        [Key(JwtOptions.SectionName, nameof(JwtOptions.Issuer))] = "starter-api-tests",
        [Key(JwtOptions.SectionName, nameof(JwtOptions.Audience))] = "starter-api-tests",
        [Key(BreakGlassOptions.SectionName, nameof(BreakGlassOptions.Password))] = NewKey(),
    };

    private static void RemoveStartupTasks(IServiceCollection services)
    {
        foreach (var runner in services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(StartupTaskRunner)).ToList())
        {
            services.Remove(runner);
        }
    }

    /// <summary>The Access module's grant port, which this host does not load (only the seeding task, removed here, calls it).</summary>
    private sealed class NoPermissionGrants : IUserPermissionGrants
    {
        public Task EnsureGrantedAsync(long userId, IReadOnlyCollection<string> permissionKeys, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

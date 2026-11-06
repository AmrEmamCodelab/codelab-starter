using System.Security.Cryptography;
using Codelab.Observability;
using Codelab.Security;
using Codelab.Security.Keys;
using Codelab.Security.Replay;
using Codelab.Security.ServiceAuth;
using Codelab.Security.Sessions;
using Codelab.Security.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Starter.Infrastructure.Configuration;
using Starter.Infrastructure.Persistence;
using Starter.Infrastructure.Startup;

namespace Starter.DocsGen.Collection;

/// <summary>
/// The real API host (Program.cs) in-process on TestServer, in Development so the OpenAPI document is mapped, and
/// without a database: migrations off, in-memory security stores, no startup task, an unreachable connection string
/// (any database use fails loudly), throwaway random keys and passwords, no log file and no Seq.
/// </summary>
internal sealed class DocsHostFactory : WebApplicationFactory<Program>
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment(Environments.Development);
        builder.UseContentRoot(AppContext.BaseDirectory);
        foreach (var (key, value) in DocsHostSettings.Create())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureAppConfiguration((_, configuration) => RemoveLocalSecretsFile(configuration.Sources));
        builder.ConfigureTestServices(services => services.RemoveAll<IStartupTask>());
    }

    /// <summary>
    /// Drops the developer's local secrets file, so a feature switch set only on one machine cannot change the
    /// generated docs (CI has no such file).
    /// </summary>
    internal static void RemoveLocalSecretsFile(IList<IConfigurationSource> sources)
    {
        var localFile = LocalSecretsFile.FileNameFor(Environments.Development);
        var local = sources.OfType<JsonConfigurationSource>()
            .Where(source => string.Equals(source.Path, localFile, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var source in local)
        {
            sources.Remove(source);
        }
    }
}

/// <summary>The configuration DocsGen starts the host with (see <see cref="DocsHostFactory"/>).</summary>
internal static class DocsHostSettings
{
    /// <summary>A connection string that no server answers; DocsGen never opens it.</summary>
    public const string UnreachableConnectionString = "Server=(localdb)\\docsgen-no-database;Database=never-opened;Connect Timeout=1";

    /// <summary>
    /// Module keys the host validates on start. They are plain keys, not module types, so DocsGen compiles without any
    /// one module (tools/remove-module.ps1); a key no module reads is ignored.
    /// </summary>
    public const string JwtSigningKeyPath = "Identity:Jwt:SigningKey";

    /// <summary>The break-glass password key (see <see cref="JwtSigningKeyPath"/>).</summary>
    public const string BreakGlassPasswordPath = "Identity:BreakGlass:Password";

    /// <summary>The first Development user's name key (see <see cref="JwtSigningKeyPath"/>).</summary>
    public const string DevUserNamePath = "Identity:DevUsers:0:UserName";

    /// <summary>The first Development user's password key (see <see cref="JwtSigningKeyPath"/>).</summary>
    public const string DevUserPasswordPath = "Identity:DevUsers:0:Password";

    /// <summary>The jobs module's job-server switch; off, so the host never opens SQL (see <see cref="JwtSigningKeyPath"/>).</summary>
    public const string JobsEnabledPath = "Jobs:Enabled";

    /// <summary>The Attachments module's service root key (see <see cref="JwtSigningKeyPath"/>); a reserved name that never resolves.</summary>
    public const string AttachmentsServiceBaseUrlPath = "Attachments:ServiceBaseUrl";

    private const string UnusedAttachmentServiceRoot = "https://attachments.invalid";
    private const string AttachmentsClientServiceName = "starter-api";
    private const string AttachmentsAudience = "starter-attachments";
    private const string Separator = ":";
    private const string KeyId = "docsgen";
    private const string DevUser = "docsgen-user";
    private const string FirstKeyIndex = "0";
    private const string SerilogMinimumLevelKey = "Serilog:MinimumLevel:Default";
    private const string QuietLogLevel = "Warning";
    private const int KeyBytes = 32;

    /// <summary>The settings; every secret is fresh random material that only lives in this process.</summary>
    public static IReadOnlyDictionary<string, string> Create()
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ConnectionStringsOptions.SectionName + Separator + nameof(ConnectionStringsOptions.Starter)] = UnreachableConnectionString,
            [DatabaseOptions.SectionName + Separator + nameof(DatabaseOptions.ApplyMigrationsOnStartup)] = bool.FalseString,
            [ReplayProtectionOptions.SectionName + Separator + nameof(ReplayProtectionOptions.Store)] = nameof(SecurityStoreKind.Memory),
            [SecuritySessionOptions.SectionName + Separator + nameof(SecuritySessionOptions.Store)] = nameof(SecurityStoreKind.Memory),
            [SqlStoreOptions.SectionName + Separator + nameof(SqlStoreOptions.EnsureSchemaOnStart)] = bool.FalseString,
            [LogFileSettings.KeyPrefix + Separator + nameof(LogFileSettings.Enabled)] = bool.FalseString,
            [SeqSettings.KeyPrefix + Separator + nameof(SeqSettings.ServerUrl)] = string.Empty,
            [SerilogMinimumLevelKey] = QuietLogLevel,
            [UserIdHashSettings.KeyName] = RandomKey(),
            [JwtSigningKeyPath] = RandomKey(),
            [BreakGlassPasswordPath] = RandomKey(),
            [DevUserNamePath] = DevUser,
            [DevUserPasswordPath] = RandomKey(),
            [JobsEnabledPath] = bool.FalseString,
            [AttachmentsServiceBaseUrlPath] = UnusedAttachmentServiceRoot,
            [ClientKey(nameof(ServiceAssertionClientOptions.ServiceName))] = AttachmentsClientServiceName,
            [ClientKey(nameof(ServiceAssertionClientOptions.Audience))] = AttachmentsAudience,
            [ClientKey(nameof(ServiceAssertionClientOptions.KeyId))] = KeyId,
            [ClientKey(nameof(ServiceAssertionClientOptions.PrivateKeyPem))] = NewServiceAssertionKeyPem(),
        };
        AddKeyRing(settings, SecuritySections.PublicIds);
        AddKeyRing(settings, SecuritySections.RequestSigning);
        return settings;
    }

    private static void AddKeyRing(Dictionary<string, string> settings, string section)
    {
        var firstKey = section + Separator + nameof(KeyRingOptions.Keys) + Separator + FirstKeyIndex + Separator;
        settings[section + Separator + nameof(KeyRingOptions.ActiveKeyId)] = KeyId;
        settings[firstKey + nameof(KeyEntryOptions.KeyId)] = KeyId;
        settings[firstKey + nameof(KeyEntryOptions.MaterialBase64)] = RandomKey();
    }

    private static string ClientKey(string name) => ServiceAssertionClientOptions.SectionName + Separator + name;

    private static string NewServiceAssertionKeyPem()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return key.ExportPkcs8PrivateKeyPem();
    }

    private static string RandomKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes));
}

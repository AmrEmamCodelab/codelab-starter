using Codelab.Security.Cors;
using Codelab.Security.Headers;
using Codelab.Security.Keys;
using Codelab.Security.PublicIds;
using Codelab.Security.RateLimiting;
using Codelab.Security.Replay;
using Codelab.Security.Sessions;
using Codelab.Security.Signing;
using Codelab.Security.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Codelab.Security.Hosting;

/// <summary>Feature-by-feature service registration behind AddCodelabSecurity (one method per feature).</summary>
internal static class SecurityRegistration
{
    internal static void AddValidatedOptions<TOptions, TValidator>(IServiceCollection services, IConfiguration configuration, string section)
        where TOptions : class
        where TValidator : class, IValidateOptions<TOptions>
    {
        services.AddOptions<TOptions>().Bind(configuration.GetSection(section)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TOptions>, TValidator>());
    }

    internal static void AddCore(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        AddValidatedOptions<SecurityProtocolOptions, SecurityProtocolOptionsValidator>(services, configuration, SecurityProtocolOptions.SectionName);
        services.TryAddSingleton(provider => provider.GetRequiredService<IOptions<SecurityProtocolOptions>>().Value.ToProtocol());
        AddValidatedOptions<KeyGuardOptions, KeyGuardOptionsValidator>(services, configuration, KeyGuardOptions.SectionName);
        services.TryAddSingleton(KeyRingGuardFactory.Create);
        AddValidatedOptions<SqlStoreOptions, SqlStoreOptionsValidator>(services, configuration, SqlStoreOptions.SectionName);
        services.AddHostedService<SqlSchemaStartup>();
        services.TryAddSingleton<SecurityStorePurgeJob>();
    }

    internal static void AddPublicIds(IServiceCollection services, IConfiguration configuration)
    {
        AddValidatedOptions<PublicIdKeyringOptions, PublicIdKeyringOptionsValidator>(services, configuration, PublicIdKeyringOptions.SectionName);
        services.TryAddSingleton(provider => PublicIdKeyring.Create(
            provider.GetRequiredService<IOptions<PublicIdKeyringOptions>>().Value,
            provider.GetRequiredService<SecurityProtocol>()));
        services.TryAddSingleton<IPublicIdCodec>(provider => new PublicIdCodec(provider.GetRequiredService<PublicIdKeyring>(), provider.GetRequiredService<SecurityProtocol>()));
        services.AddPublicIdBinding();
    }

    internal static void AddSigning(IServiceCollection services, IConfiguration configuration)
    {
        AddValidatedOptions<RequestSigningOptions, RequestSigningOptionsValidator>(services, configuration, RequestSigningOptions.SectionName);
        AddValidatedOptions<ResponseSigningOptions, ResponseSigningOptionsValidator>(services, configuration, ResponseSigningOptions.SectionName);
        services.TryAddSingleton<ISigningKeyring>(provider => SigningKeyring.Create(provider.GetRequiredService<IOptionsMonitor<RequestSigningOptions>>().CurrentValue));
        services.TryAddSingleton(provider => new SessionKeyDerivation(provider.GetRequiredService<SecurityProtocol>()));
        services.TryAddSingleton<IProofVerifier>(provider => new HmacProofVerifier(
            provider.GetRequiredService<ISigningKeyring>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<SecurityProtocol>(),
            provider.GetRequiredService<IOptionsMonitor<RequestSigningOptions>>().CurrentValue));
        services.TryAddSingleton<SigningKeyIssuer>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<Microsoft.AspNetCore.Hosting.IStartupFilter, ResponseSigningStartupFilter>());
    }

    internal static void AddReplay(IServiceCollection services, IConfiguration configuration)
    {
        AddValidatedOptions<ReplayProtectionOptions, ReplayProtectionOptionsValidator>(services, configuration, ReplayProtectionOptions.SectionName);
        services.TryAddSingleton<IReplayGuard>(provider => provider.GetRequiredService<IOptions<ReplayProtectionOptions>>().Value is { Store: SecurityStoreKind.SqlServer }
            ? new SqlReplayGuard(provider.GetRequiredService<IOptions<SqlStoreOptions>>().Value, provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILogger<SqlReplayGuard>>())
            : new MemoryReplayGuard(provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<IOptions<ReplayProtectionOptions>>().Value.MemoryEntryLimit));
        services.AddSingleton<IExpiredEntryPurger>(provider => provider.GetRequiredService<IReplayGuard>() as IExpiredEntryPurger ?? NoPurge.Instance);
    }

    internal static void AddSessions(IServiceCollection services, IConfiguration configuration)
    {
        AddValidatedOptions<SecuritySessionOptions, SessionOptionsValidator>(services, configuration, SecuritySessionOptions.SectionName);
        services.TryAddSingleton<ISessionVersionStore>(provider => UsesSql(provider)
            ? new SqlSessionVersionStore(provider.GetRequiredService<IOptions<SqlStoreOptions>>().Value, provider.GetRequiredService<TimeProvider>())
            : new MemorySessionVersionStore());
        services.TryAddSingleton<ISessionRevocationStore>(provider => UsesSql(provider)
            ? new SqlSessionRevocationStore(provider.GetRequiredService<IOptions<SqlStoreOptions>>().Value, provider.GetRequiredService<TimeProvider>())
            : new MemorySessionRevocationStore(provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IExpiredEntryPurger>(provider => provider.GetRequiredService<ISessionRevocationStore>() as IExpiredEntryPurger ?? NoPurge.Instance);
    }

    internal static void AddEdge(IServiceCollection services, IConfiguration configuration)
    {
        AddValidatedOptions<SecurityHeaderOptions, SecurityHeaderOptionsValidator>(services, configuration, SecurityHeaderOptions.SectionName);
        services.TryAddEnumerable(ServiceDescriptor.Transient<Microsoft.AspNetCore.Hosting.IStartupFilter, SecurityHeadersStartupFilter>());
        AddValidatedOptions<CodelabCorsOptions, CodelabCorsOptionsValidator>(services, configuration, CodelabCorsOptions.SectionName);
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptions<CodelabCorsOptions>, SecurityProtocol>((cors, codelab, protocol) => CorsOriginValidator.Configure(cors, codelab.Value, protocol));
        AddValidatedOptions<ClientRateLimitOptions, ClientRateLimitOptionsValidator>(services, configuration, ClientRateLimitOptions.SectionName);
        services.TryAddSingleton<ClientAddressResolver>();
        services.AddRateLimiter(_ => { });
        services.AddOptions<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>().Configure<ClientAddressResolver, IOptions<ClientRateLimitOptions>>(ClientRateLimiting.Configure);
    }

    private static bool UsesSql(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<SecuritySessionOptions>>().Value.Store is SecurityStoreKind.SqlServer;

    /// <summary>Stands in for a host-supplied store that has nothing to purge.</summary>
    private sealed class NoPurge : IExpiredEntryPurger
    {
        internal static readonly NoPurge Instance = new();

        public string StoreName => nameof(NoPurge);

        public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }
}

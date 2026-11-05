using Codelab.Identity.ActiveDirectory;
using Codelab.Identity.Oidc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Codelab.Identity;

/// <summary>
/// Registration of the two providers. Each binds its section, validates it on start through its deployment guard
/// (which needs the host's <c>IHostEnvironment</c>) and registers its services with TryAdd, so a host or a test can
/// put its own <see cref="ILdapConnectionFactory"/>, <see cref="IIdTokenSigningKeySource"/> or <see cref="TimeProvider"/> first.
/// </summary>
public static class CodelabIdentityExtensions
{
    /// <summary>
    /// Binds <see cref="ActiveDirectoryOptions.SectionName"/> (validated on start) and registers
    /// <see cref="IActiveDirectoryAuthenticator"/>, <see cref="IActiveDirectoryConnectionProbe"/>,
    /// <see cref="LdapsCertificateReader"/> and the production <see cref="ILdapConnectionFactory"/>.
    /// </summary>
    public static IServiceCollection AddCodelabActiveDirectory(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ActiveDirectoryOptions>().Bind(configuration.GetSection(ActiveDirectoryOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ActiveDirectoryOptions>, ActiveDirectoryOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ILdapConnectionFactory, SystemLdapConnectionFactory>();
        services.TryAddSingleton<IActiveDirectoryAuthenticator, LdapActiveDirectoryAuthenticator>();
        services.TryAddSingleton<IActiveDirectoryConnectionProbe, ActiveDirectoryConnectionProbe>();
        services.TryAddSingleton<LdapsCertificateReader>();
        return services;
    }

    /// <summary>
    /// Binds <see cref="OidcOptions.SectionName"/> (validated on start), registers the named back-channel client
    /// <see cref="OidcHttpClient.Name"/>, the signing-key source of the configured <see cref="SigningKeyMode"/>,
    /// <see cref="OidcIdTokenValidator"/> and <see cref="IOidcIdentityProvider"/>.
    /// </summary>
    public static IServiceCollection AddCodelabOidc(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<OidcOptions>().Bind(configuration.GetSection(OidcOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<OidcOptions>, OidcOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(OidcHttpClient.Name).ConfigureHttpClient((provider, client) =>
        {
            client.Timeout = provider.GetRequiredService<IOptions<OidcOptions>>().Value.BackchannelTimeout;
            client.MaxResponseContentBufferSize = OidcHttpClient.MaxResponseBytes;
        });
        services.TryAddSingleton<JwksSigningKeySource>();
        services.TryAddSingleton<IIdTokenSigningKeySource>(KeySourceFor);
        services.TryAddSingleton<OidcIdTokenValidator>();
        services.TryAddSingleton<IOidcIdentityProvider, OidcIdentityProvider>();
        return services;
    }

    private static IIdTokenSigningKeySource KeySourceFor(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<OidcOptions>>();
        return options.Value.SigningKeyMode == SigningKeyMode.Pinned
            ? new PinnedSigningKeySource(options)
            : provider.GetRequiredService<JwksSigningKeySource>();
    }
}

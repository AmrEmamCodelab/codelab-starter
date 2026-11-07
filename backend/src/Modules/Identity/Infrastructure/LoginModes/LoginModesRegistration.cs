using Codelab.Identity;
using Codelab.Identity.ActiveDirectory;
using Codelab.Identity.Oidc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Starter.Application.SingleSignOn;
using Starter.Application.Validation;
using Starter.Modules.Identity.Application.LoginModes;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Infrastructure.LoginModes;

/// <summary>The login modes' registrations (called by <see cref="IdentityModule"/> after <c>AddIdentityModule</c>).</summary>
public static class LoginModesRegistration
{
    /// <summary>
    /// Registers <c>Auth</c> (validated on start: a usable mode list and, with <c>sso</c>, the SPA base URL and sealed
    /// tickets), the provider of every listed mode (<c>ad</c>: <see cref="CodelabIdentityExtensions.AddCodelabActiveDirectory"/>,
    /// <c>sso</c>: <see cref="CodelabIdentityExtensions.AddCodelabOidc"/>) switched on so that its deployment guard runs at
    /// start, the adapters over them and the login-mode use cases. With <c>sso</c> it also offers the kernel
    /// <see cref="IIdentityProviderKeySet"/> over the SSO back channel (the Ops certificate list reads the provider's
    /// signing certificates through it). Basic login decides in its use case (break-glass only while basic is off), so
    /// it has no gate here.
    /// </summary>
    public static IServiceCollection AddIdentityLoginModes(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(AuthModesOptions.SectionName);
        services.AddOptions<AuthModesOptions>().Configure(options => AuthModesOptions.Bind(section, options)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AuthModesOptions>, AuthModesOptionsValidator>());
        AddProviders(services, configuration, ListedModes(section));
        AddPorts(services);
        AddUseCases(services);
        return services;
    }

    /// <summary>Maps nothing: the login-mode endpoints are the controllers in <c>Api/LoginModes</c>.</summary>
    public static IEndpointRouteBuilder UseIdentityLoginModes(this IEndpointRouteBuilder endpoints) => endpoints;

    /// <summary>
    /// The modes listed at registration, read leniently (an unusable list registers no provider and is refused by
    /// <see cref="AuthModesOptionsValidator"/> when the host starts, so every configuration error surfaces the same way).
    /// </summary>
    private static AuthModeList ListedModes(IConfigurationSection section)
    {
        var options = new AuthModesOptions();
        AuthModesOptions.Bind(section, options);
        return AuthModeNames.Parse(options.Modes);
    }

    private static void AddProviders(IServiceCollection services, IConfiguration configuration, AuthModeList modes)
    {
        if (modes.Contains(SignInMode.ActiveDirectory))
        {
            services.AddCodelabActiveDirectory(configuration);
            services.PostConfigure<ActiveDirectoryOptions>(options => options.Enabled = true);
        }

        if (modes.Contains(SignInMode.Sso))
        {
            AddSsoBackChannel(services, configuration);
            services.AddCodelabOidc(configuration);
            services.PostConfigure<OidcOptions>(options => options.Enabled = true);
            services.TryAddSingleton<IIdentityProviderKeySet, SsoIdentityProviderKeySet>();
        }
    }

    /// <summary>
    /// With <c>Codelab:Identity:Sso:BackChannel</c> 2 (GatewayRelay), registers the integration gateway client
    /// (<c>Codelab:Integration:Gateway</c>, validated on start) and <see cref="GatewaySsoBackChannel"/> before
    /// <c>AddCodelabOidc</c>, which then keeps it. Read leniently: an unreadable value registers nothing here and is
    /// refused by the SSO deployment guard on start.
    /// </summary>
    private static void AddSsoBackChannel(IServiceCollection services, IConfiguration configuration)
    {
        if (ConfiguredBackChannel(configuration) == OidcBackChannel.GatewayRelay)
        {
            services.AddGatewayClientOnce(configuration);
            services.TryAddSingleton<IOidcBackChannel, GatewaySsoBackChannel>();
        }
    }

    private static OidcBackChannel? ConfiguredBackChannel(IConfiguration configuration)
    {
        try
        {
            return configuration.GetSection(OidcOptions.SectionName).GetValue<OidcBackChannel?>(nameof(OidcOptions.BackChannel));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void AddPorts(IServiceCollection services)
    {
        services.AddSingleton<ILoginModes, LoginModeSet>();
        services.AddSingleton<ISsoSpaCallback, ConfiguredSsoSpaCallback>();
        services.AddSingleton<IDirectorySignIn, ActiveDirectorySignIn>();
        services.AddSingleton<ISsoProvider, OidcSsoProvider>();
        services.AddSingleton<ISsoTickets, SealedSsoTickets>();
    }

    private static void AddUseCases(IServiceCollection services)
    {
        services.AddSingleton<IValidator<DirectoryLoginCommand>, DirectoryLoginCommandValidator>();
        services.AddSingleton<IValidator<SsoExchangeCommand>, SsoExchangeCommandValidator>();
        services.AddSingleton<LoginModesQuery>();
        services.AddScoped<BasicLoginAccounts>();
        services.AddScoped<DirectoryLoginUseCase>();
        services.AddScoped<BeginSsoSignIn>();
        services.AddScoped<CompleteSsoSignIn>();
        services.AddScoped<ExchangeSsoTicket>();
        services.AddScoped<SsoLogoutUseCase>();
    }
}

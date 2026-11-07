using Codelab.Integration.Outbound;
using Codelab.Security;
using Codelab.Security.ServiceAuth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Codelab.Integration.Gateway;

/// <summary>Registers the integration gateway: the caller's relay client, and the gateway's profiles.</summary>
public static class GatewayServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="GatewayRelayClient"/> as a Codelab outbound client named
    /// <see cref="GatewayRelayClient.Name"/>: base address, retry, timeouts and certificate trust from
    /// <see cref="GatewayOptions.SectionName"/>, and a handler inside the retry policy that signs every attempt with
    /// an ES256 service assertion (this service's identity from <c>Codelab:Security:ServiceAssertions:Client</c>, the
    /// audience from <see cref="GatewayOptions.Audience"/>). Every option is validated at start-up. Requires
    /// <c>AddCodelabSecurity</c> (the protocol).
    /// </summary>
    public static IHttpClientBuilder AddCodelabGatewayClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        AddGatewayOptions(services, configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<GatewayOptions>, GatewayClientOptionsValidator>(
            provider => new GatewayClientOptionsValidator(IsDevelopment(provider))));
        services.AddOptions<OutboundClientOptions>(GatewayRelayClient.Name)
            .Configure<IOptions<GatewayOptions>, IServiceProvider>((outbound, gateway, provider) => ApplyOutbound(outbound, gateway.Value, IsDevelopment(provider)))
            .ValidateOnStart();
        AddAssertionIssuer(services, configuration);

        var builder = OutboundClientServiceCollectionExtensions.AddClient<GatewayRelayClient>(services, GatewayRelayClient.Name);
        builder.AddHttpMessageHandler(provider => new ServiceAssertionHandler(
            provider.GetRequiredService<GatewayAssertionIssuer>().Issuer,
            provider.GetRequiredService<SecurityProtocol>()));
        return builder;
    }

    /// <summary>
    /// The gateway side: binds <see cref="GatewayOptions"/> (the enabled <see cref="GatewayOptions.Profiles"/>),
    /// validates it at start-up and registers the <see cref="GatewayProfileRegistry"/>. Map the profiles with
    /// <c>MapGatewayProfiles()</c>.
    /// </summary>
    public static IServiceCollection AddCodelabGatewayProfiles(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        AddGatewayOptions(services, configuration);
        services.TryAddSingleton<GatewayProfileRegistry>();
        return services;
    }

    /// <summary>
    /// Registers the relay profile <typeparamref name="TProfile"/> (a singleton). It is mapped only when its key is in
    /// <see cref="GatewayOptions.Profiles"/>; registering two profiles with one key fails the start.
    /// </summary>
    public static IServiceCollection AddGatewayProfile<TProfile>(this IServiceCollection services)
        where TProfile : class, IGatewayProfile
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGatewayProfile, TProfile>());
        services.TryAddSingleton<GatewayProfileRegistry>();
        return services;
    }

    internal static void ApplyOutbound(OutboundClientOptions outbound, GatewayOptions gateway, bool isDevelopment)
    {
        outbound.BaseAddress = WithTrailingSlash(gateway.BaseUrl);
        outbound.AllowPlainHttp = isDevelopment;
        outbound.Retry = gateway.Retry;
        outbound.Timeout = gateway.Timeout;
        outbound.Certificate = gateway.Certificate;
    }

    /// <summary>The base address with a trailing slash, so relative relay routes keep the gateway's path base.</summary>
    internal static Uri? WithTrailingSlash(Uri? baseUrl)
    {
        if (baseUrl is null || !baseUrl.IsAbsoluteUri || baseUrl.AbsolutePath.EndsWith('/'))
        {
            return baseUrl;
        }

        return new UriBuilder(baseUrl) { Path = baseUrl.AbsolutePath + "/" }.Uri;
    }

    private static void AddGatewayOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GatewayOptions>().Bind(configuration.GetSection(GatewayOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<GatewayOptions>, GatewayOptionsValidator>());
    }

    private static void AddAssertionIssuer(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ServiceAssertionClientOptions>(GatewayRelayClient.Name)
            .Bind(configuration.GetSection(ServiceAssertionClientOptions.SectionName))
            .Configure<IOptions<GatewayOptions>>((assertion, gateway) => assertion.Audience = gateway.Value.Audience)
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ServiceAssertionClientOptions>, GatewayAssertionOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<GatewayAssertionIssuer>();
    }

    private static bool IsDevelopment(IServiceProvider provider) => provider.GetService<IHostEnvironment>()?.IsDevelopment() ?? false;
}

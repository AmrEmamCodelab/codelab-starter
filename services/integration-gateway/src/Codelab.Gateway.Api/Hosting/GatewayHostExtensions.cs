using Codelab.Gateway.Api.Authentication;
using Codelab.Gateway.Api.Diagnostics;
using Codelab.Gateway.Api.Sms; // codelab:module Sms
using Codelab.Gateway.Api.Sso;
using Codelab.Integration.Gateway;
using Codelab.Observability;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Codelab.Gateway.Api.Hosting;

/// <summary>The host wiring Program.cs calls: services (<see cref="AddGatewayHost"/>) and the pipeline.</summary>
public static class GatewayHostExtensions
{
    /// <summary>The name of the upstream readiness check.</summary>
    public const string UpstreamCheckName = "gateway-upstreams";

    /// <summary>
    /// Registers the service-assertion scheme and its policy (the default and the fallback), the profile registry, the
    /// two built-in relay profiles (an enabled one also registers its upstream client and validates its section at
    /// start), the upstream readiness check, and the request ceiling. Call it after <c>AddCodelabSecurity</c>.
    /// </summary>
    public static IServiceCollection AddGatewayHost(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var enabled = GatewayHostSettings.EnabledProfilesFrom(configuration);
        services.AddGatewayAuthentication(configuration);
        services.AddCodelabGatewayProfiles(configuration);
        services.AddSsoRelay(configuration, enabled: enabled.Contains(GatewayProfileKey.Sso));
        services.AddSmsRelay(configuration, enabled: enabled.Contains(GatewayProfileKey.Sms)); // codelab:module Sms
        services.AddOptions<GatewayReadinessOptions>().Bind(configuration.GetSection(GatewayReadinessOptions.SectionName));
        services.TryAddSingleton<IUpstreamHostResolver, DnsUpstreamHostResolver>();
        services.AddHealthChecks().AddCheck<UpstreamReadinessCheck>(UpstreamCheckName, tags: [HealthEndpoints.ReadyTag]);
        services.Configure<KestrelServerOptions>(kestrel => kestrel.Limits.MaxRequestBodySize = GatewayHostSettings.MaxRequestBodyBytes);
        services.Configure<IISServerOptions>(iis => iis.MaxRequestBodySize = GatewayHostSettings.MaxRequestBodyBytes);
        return services;
    }

    /// <summary>Routing, authentication and authorization (no CORS: the gateway has no browser caller).</summary>
    public static IApplicationBuilder UseGatewayPipeline(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    /// <summary>
    /// Maps every enabled profile's relay routes behind the service-caller policy, and answers the routes of a disabled
    /// profile with 404 <c>GATEWAY_PROFILE_DISABLED</c> (after authentication: an unsigned caller learns nothing).
    /// </summary>
    public static IEndpointRouteBuilder MapGatewayRelays(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGroup(string.Empty).RequireAuthorization(GatewayAuthentication.ServiceCallerPolicy).MapGatewayProfiles();
        return endpoints;
    }
}

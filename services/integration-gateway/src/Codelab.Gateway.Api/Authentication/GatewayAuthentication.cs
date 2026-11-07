using Codelab.Security.ServiceAuth;
using Codelab.Security.Sessions;
using Microsoft.AspNetCore.Authorization;

namespace Codelab.Gateway.Api.Authentication;

/// <summary>
/// The gateway's one credential: the Codelab.Security ES256 service assertion (scheme
/// <see cref="ServiceAssertionAuthenticationHandler.SchemeName"/>), validated against
/// <c>Codelab:Security:ServiceAssertions:Server</c> (audience, trusted caller keys, lifetime, body ceiling), with the
/// jti replay guard and the method, path and body binding. There is no default scheme, no JWT bearer and no API key:
/// an <c>Authorization</c> or API-key header is never read, so it authenticates nothing and the request is challenged
/// with 401. The policy is the default and the fallback, so a relay route mapped without a policy is still S2S only.
/// </summary>
public static class GatewayAuthentication
{
    /// <summary>A valid ES256 service assertion from a trusted service.</summary>
    public const string ServiceCallerPolicy = "gateway-service-caller";

    internal static IServiceCollection AddGatewayAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication().AddCodelabServiceAssertion(configuration);
        var serviceCaller = new AuthorizationPolicyBuilder(ServiceAssertionAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser()
            .RequireClaim(SecurityClaimTypes.Service)
            .Build();
        services.AddAuthorizationBuilder()
            .AddPolicy(ServiceCallerPolicy, serviceCaller)
            .SetDefaultPolicy(serviceCaller)
            .SetFallbackPolicy(serviceCaller);
        return services;
    }
}

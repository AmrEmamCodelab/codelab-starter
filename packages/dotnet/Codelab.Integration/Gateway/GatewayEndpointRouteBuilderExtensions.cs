using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Codelab.Integration.Gateway;

/// <summary>Maps the gateway's relay profiles.</summary>
public static class GatewayEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps every enabled profile (<see cref="IGatewayProfile.Map"/>), and answers every route of a registered but
    /// disabled profile with 404 <see cref="GatewayFailureCodes.ProfileDisabled"/>. Resolving the registry here fails
    /// the start on a misconfigured profile set. Call it on a group that requires the service-assertion scheme.
    /// </summary>
    public static IEndpointRouteBuilder MapGatewayProfiles(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var registry = endpoints.ServiceProvider.GetRequiredService<GatewayProfileRegistry>();
        foreach (var profile in registry.Enabled)
        {
            profile.Map(endpoints);
        }

        foreach (var profile in registry.Disabled)
        {
            var prefix = GatewayRoutes.ProfilePrefix(profile.Key);
            endpoints.Map(prefix, () => GatewayProblems.For(GatewayFailureCodes.ProfileDisabled));
            endpoints.Map(prefix + GatewayRoutes.CatchAllSuffix, () => GatewayProblems.For(GatewayFailureCodes.ProfileDisabled));
        }

        return endpoints;
    }
}

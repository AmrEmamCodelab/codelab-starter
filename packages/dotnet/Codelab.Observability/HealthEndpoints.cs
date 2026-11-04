using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Codelab.Observability;

/// <summary>
/// The two health endpoints every Codelab host exposes, anonymous and outside request tracing:
/// <list type="bullet">
/// <item><see cref="LivePath"/>: the process answers. Runs NO check, so a database outage never makes a load balancer
///   or IIS recycle a healthy process.</item>
/// <item><see cref="ReadyPath"/>: the host can serve. Runs every check tagged <see cref="ReadyTag"/> (a host adds
///   them with <c>services.AddHealthChecks().AddCheck(..., tags: [HealthEndpoints.ReadyTag])</c>).</item>
/// </list>
/// </summary>
public static class HealthEndpoints
{
    /// <summary>The common prefix of both endpoints, quiet in the request log and untraced.</summary>
    public const string BasePath = "/health";

    /// <summary>The liveness endpoint.</summary>
    public const string LivePath = BasePath + "/live";

    /// <summary>The readiness endpoint.</summary>
    public const string ReadyPath = BasePath + "/ready";

    /// <summary>The tag of a check that readiness runs.</summary>
    public const string ReadyTag = "ready";

    /// <summary>Maps <see cref="LivePath"/> and <see cref="ReadyPath"/>. The health-check services are registered by <c>AddCodelabObservability</c>.</summary>
    public static IEndpointRouteBuilder MapCodelabHealth(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks(LivePath, new HealthCheckOptions { Predicate = static _ => false }).AllowAnonymous();
        endpoints.MapHealthChecks(ReadyPath, new HealthCheckOptions { Predicate = IsReadinessCheck }).AllowAnonymous();
        return endpoints;
    }

    internal static bool IsReadinessCheck(HealthCheckRegistration registration) => registration.Tags.Contains(ReadyTag);
}

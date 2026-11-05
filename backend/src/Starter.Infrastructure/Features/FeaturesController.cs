using Codelab.Security.Signing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Infrastructure.Hosting;

namespace Starter.Infrastructure.Features;

/// <summary>The feature flags of this deployment, as the client reads them.</summary>
/// <param name="Flags">Every declared flag name to its effective value. Flags only: never an identity or a setting.</param>
public sealed record FeaturesResponse(IReadOnlyDictionary<string, bool> Flags);

/// <summary>The routes of the kernel's own endpoints.</summary>
public static class KernelRoutes
{
    /// <summary>The anonymous feature-flag endpoint (<c>GET /api/features</c>).</summary>
    public const string Features = ApiRoutes.Prefix + ApiRoutes.Separator + "features";
}

/// <summary>
/// <c>GET /api/features</c>: the deployment's feature flags, anonymous because the login screen needs them before
/// anyone signs in. It discloses flag names and on/off values only.
/// </summary>
[ApiController]
[Route(KernelRoutes.Features)]
[AllowAnonymous]
public sealed class FeaturesController(IFeatureGate gate) : ControllerBase
{
    /// <summary>Why the endpoint is anonymous and unsigned.</summary>
    public const string AnonymousReason = "Feature flags are read before sign-in; the response holds flag values only.";

    /// <summary>Every declared flag and its effective value.</summary>
    [HttpGet]
    [SignatureExempt(SignatureExemption.WholeRequest, AnonymousReason)]
    [ProducesResponseType<FeaturesResponse>(StatusCodes.Status200OK)]
    public ActionResult<FeaturesResponse> Get() => new FeaturesResponse(gate.Flags);
}

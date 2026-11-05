using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Application.Results;
using Starter.Domain.Exceptions;
using Starter.Infrastructure.Features;
using Starter.Infrastructure.Modularity;
using Starter.Infrastructure.Results;

namespace Starter.Api.Tests.Support;

/// <summary>Names and routes of the probe module.</summary>
public static class Probe
{
    public const string ModuleName = "Probe";
    public const string Flag = "probeFeature";
    public const string Route = "api/probe";
    public const string ExtraRoute = "/api/probe-extra";
    public const string RuleCode = "PROBE_RULE";
    public const string ConflictCode = "PROBE_CONFLICT";
}

/// <summary>A module that declares a feature flag, maps one extra endpoint and contributes <see cref="ProbeController"/>.</summary>
public sealed class ProbeModule : IModule
{
    public string Name => Probe.ModuleName;

    public bool Added { get; private set; }

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        Added = true;
        services.AddSingleton<IFeatureFlagSource, ProbeFlags>();
    }

    public void UseModule(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet(Probe.ExtraRoute, () => Results.Ok()).AllowAnonymous();
}

/// <summary>A second module with the same name, for the duplicate checks.</summary>
public sealed class DuplicateProbeModule : IModule
{
    public string Name => Probe.ModuleName;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void UseModule(IEndpointRouteBuilder endpoints)
    {
    }
}

public sealed class ProbeFlags : IFeatureFlagSource
{
    public IReadOnlyCollection<FeatureFlag> Flags { get; } = [new FeatureFlag(Probe.Flag, DefaultValue: false, Description: "Probe.")];
}

[ApiController]
[Route(Probe.Route)]
public sealed class ProbeController : ControllerBase
{
    [HttpGet("gated")]
    [AllowAnonymous]
    [RequiresFeature(Probe.Flag)]
    public IActionResult Gated() => Ok();

    [HttpGet("missing")]
    [AllowAnonymous]
    public IActionResult Missing() => throw new ResourceNotFoundException(ControllerContext.ActionDescriptor.ActionName);

    [HttpGet("rule")]
    [AllowAnonymous]
    public IActionResult Rule() => throw new BusinessRuleException(Probe.RuleCode, ControllerContext.ActionDescriptor.ActionName);

    [HttpGet("denied")]
    [AllowAnonymous]
    public IActionResult Denied() => throw new AccessDeniedException(ControllerContext.ActionDescriptor.ActionName);

    [HttpGet("refused")]
    [AllowAnonymous]
    public ActionResult<string> Refused() => Outcome.Refused<string>(Refusal.Conflict(Probe.ConflictCode)).ToActionResult(value => value + Request.Path);

    [HttpGet("secure")]
    [Authorize]
    public IActionResult Secure() => Ok();
}

/// <summary>A protected controller without any attribute: the fallback policy must close it.</summary>
[ApiController]
[Route(Probe.Route + "/unmarked")]
public sealed class UnmarkedController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}

using Microsoft.AspNetCore.Mvc;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Hosting;
using Starter.Infrastructure.Results;
using Starter.Modules.Access.Application;

namespace Starter.Modules.Access.Api;

/// <summary>The Access routes, built from the kernel prefix (no route literal elsewhere).</summary>
public static class AccessRoutes
{
    /// <summary>The caller's own permissions: <c>GET /api/me/permissions</c>.</summary>
    public const string MyPermissions = ApiRoutes.Prefix + ApiRoutes.Separator + "me" + ApiRoutes.Separator + "permissions";

    /// <summary>The dashboard: <c>GET /api/dashboard</c>.</summary>
    public const string Dashboard = ApiRoutes.Prefix + ApiRoutes.Separator + "dashboard";

    /// <summary>The access administration area: <c>GET /api/access/admin</c>.</summary>
    public const string AccessAdmin = ApiRoutes.Prefix + ApiRoutes.Separator + "access" + ApiRoutes.Separator + "admin";
}

/// <summary>The body of <c>GET /api/me/permissions</c>.</summary>
/// <param name="Permissions">The permission keys the caller holds now, sorted.</param>
public sealed record MyPermissionsResponse(IReadOnlyList<string> Permissions);

/// <summary>The body of <c>GET /api/dashboard</c> (empty in phase 2).</summary>
public sealed record DashboardResponse;

/// <summary>The body of <c>GET /api/access/admin</c> (an empty placeholder in phase 2).</summary>
public sealed record AccessAdminResponse;

/// <summary>The caller's own permissions. Any signed-in user may read them (allowlisted).</summary>
[ApiController]
[Route(AccessRoutes.MyPermissions)]
[AuthenticatedUser]
public sealed class MyPermissionsController(GetMyPermissions getMyPermissions) : ControllerBase
{
    /// <summary>The permission keys the caller holds now.</summary>
    [HttpGet]
    public async Task<ActionResult<MyPermissionsResponse>> Get(CancellationToken cancellationToken) =>
        (await getMyPermissions.ExecuteAsync(cancellationToken)).ToActionResult(mine => new MyPermissionsResponse(mine.Permissions));
}

/// <summary>The dashboard.</summary>
[ApiController]
[Route(AccessRoutes.Dashboard)]
[HasPermission(AccessPermissionKeys.DashboardView)]
public sealed class DashboardController(GetDashboard getDashboard) : ControllerBase
{
    /// <summary>The dashboard's data.</summary>
    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> Get(CancellationToken cancellationToken) =>
        (await getDashboard.ExecuteAsync(cancellationToken)).ToActionResult(_ => new DashboardResponse());
}

/// <summary>The access administration area (placeholder until the phase 3 role and grant editor).</summary>
[ApiController]
[Route(AccessRoutes.AccessAdmin)]
[HasPermission(AccessPermissionKeys.AccessAdminView)]
public sealed class AccessAdminController(GetAccessAdminOverview getOverview) : ControllerBase
{
    /// <summary>The access administration overview.</summary>
    [HttpGet]
    public async Task<ActionResult<AccessAdminResponse>> Get(CancellationToken cancellationToken) =>
        (await getOverview.ExecuteAsync(cancellationToken)).ToActionResult(_ => new AccessAdminResponse());
}

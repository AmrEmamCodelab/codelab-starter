using Codelab.Security.PublicIds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Application.Results;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Results;
using Starter.Modules.Access.Application;

namespace Starter.Modules.Access.Api;

/// <summary>The permission catalog for the role and grant editors.</summary>
[ApiController]
[Route(AccessRoutes.Permissions)]
public sealed class AccessPermissionsController(ListPermissions listPermissions) : ControllerBase
{
    /// <summary>Every catalog permission, retired ones included, sorted by key.</summary>
    [HttpGet]
    [HasPermission(AccessPermissionKeys.RolesView)]
    public async Task<ActionResult<IReadOnlyList<PermissionResponse>>> List(CancellationToken cancellationToken) =>
        (await listPermissions.ExecuteAsync(cancellationToken)).ToActionResult(rows => (IReadOnlyList<PermissionResponse>)[.. rows.Select(PermissionResponse.From)]);
}

/// <summary>The roles: list, read, create, update (rename, permissions, deactivate or reactivate).</summary>
[ApiController]
[Route(AccessRoutes.Roles)]
public sealed class AccessRolesController(ListRoles listRoles, GetRole getRole, CreateRole createRole, UpdateRole updateRole) : ControllerBase
{
    /// <summary>Every role, virtual roles first.</summary>
    [HttpGet]
    [HasPermission(AccessPermissionKeys.RolesView)]
    public async Task<ActionResult<IReadOnlyList<RoleSummaryResponse>>> List(CancellationToken cancellationToken) =>
        (await listRoles.ExecuteAsync(cancellationToken)).ToActionResult(rows => (IReadOnlyList<RoleSummaryResponse>)[.. rows.Select(RoleSummaryResponse.From)]);

    /// <summary>One role with its permissions.</summary>
    [HttpGet(AccessRoutes.RoleIdTemplate)]
    [HasPermission(AccessPermissionKeys.RolesView)]
    public async Task<ActionResult<RoleResponse>> Get(PublicId<RoleIds> id, CancellationToken cancellationToken) =>
        (await getRole.ExecuteAsync(id.Value, cancellationToken)).ToActionResult(RoleResponse.From);

    /// <summary>Creates an active, ordinary role (201).</summary>
    [HttpPost]
    [HasPermission(AccessPermissionKeys.RolesManage)]
    [ProducesResponseType<RoleResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<RoleResponse>> Create(CreateRoleCommand command, CancellationToken cancellationToken) =>
        (await createRole.ExecuteAsync(command, cancellationToken)).ToCreatedResult(RoleResponse.From);

    /// <summary>Changes a role's names, permissions and active state.</summary>
    [HttpPut(AccessRoutes.RoleIdTemplate)]
    [HasPermission(AccessPermissionKeys.RolesManage)]
    public async Task<ActionResult<RoleResponse>> Update(PublicId<RoleIds> id, UpdateRoleCommand command, CancellationToken cancellationToken) =>
        (await updateRole.ExecuteAsync(id.Value, command, cancellationToken)).ToActionResult(RoleResponse.From);
}

/// <summary>The users of the grant editor: the picker and each user's roles and direct grants.</summary>
[ApiController]
[Route(AccessRoutes.Users)]
public sealed class AccessUsersController(SearchAccessUsers searchUsers, GetUserGrants getGrants, PutUserGrants putGrants) : ControllerBase
{
    /// <summary>Up to 20 accounts matching the search (active and deactivated).</summary>
    [HttpGet]
    [HasPermission(AccessPermissionKeys.GrantsView)]
    public async Task<ActionResult<IReadOnlyList<AccessUserResponse>>> Search([FromQuery(Name = AccessRoutes.SearchParameter)] string? search, CancellationToken cancellationToken) =>
        (await searchUsers.ExecuteAsync(search, cancellationToken)).ToActionResult(rows => (IReadOnlyList<AccessUserResponse>)[.. rows.Select(AccessUserResponse.From)]);

    /// <summary>One user's roles, direct grants, virtual roles and effective permissions.</summary>
    [HttpGet(AccessRoutes.UserGrantsTemplate)]
    [HasPermission(AccessPermissionKeys.GrantsView)]
    public async Task<ActionResult<UserGrantsResponse>> GetGrants(PublicId<UserIds> userId, CancellationToken cancellationToken) =>
        (await getGrants.ExecuteAsync(userId.Value, cancellationToken)).ToActionResult(UserGrantsResponse.From);

    /// <summary>Replaces one user's roles and direct grants (live on the user's next request, no relogin).</summary>
    [HttpPut(AccessRoutes.UserGrantsTemplate)]
    [HasPermission(AccessPermissionKeys.GrantsManage)]
    public async Task<ActionResult<UserGrantsResponse>> PutGrants(PublicId<UserIds> userId, PutUserGrantsCommand command, CancellationToken cancellationToken) =>
        (await putGrants.ExecuteAsync(userId.Value, command, cancellationToken)).ToActionResult(UserGrantsResponse.From);
}

/// <summary>The 201 mapping the kernel <c>OutcomeResults</c> does not have yet (one owner in this module).</summary>
public static class AccessResults
{
    /// <summary>201 with <paramref name="map"/> of the value, or the refusal's problem+json.</summary>
    public static ActionResult<TResponse> ToCreatedResult<T, TResponse>(this Outcome<T> outcome, Func<T, TResponse> map)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(map);
        return outcome.IsSuccess
            ? new ObjectResult(map(outcome.Value)) { StatusCode = StatusCodes.Status201Created }
            : outcome.Refusal.ToActionResult();
    }
}

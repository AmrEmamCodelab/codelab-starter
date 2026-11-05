using Codelab.Security.PublicIds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Results;
using Starter.Modules.Users.Application;
using Starter.Modules.Users.Application.Organizations;

namespace Starter.Modules.Users.Api;

/// <summary>The organizations. Thin: each action calls one use case and maps its outcome.</summary>
[ApiController]
[Route(UsersRoutes.Organizations)]
[Produces(UsersRoutes.JsonContentType)]
public sealed class AdminOrganizationsController(
    ListOrganizations listOrganizations,
    GetOrganization getOrganization,
    CreateOrganization createOrganization,
    UpdateOrganization updateOrganization,
    SetOrganizationActive setOrganizationActive) : ControllerBase
{
    /// <summary>One page of organizations, ordered by code.</summary>
    [HttpGet]
    [HasPermission(UsersPermissionKeys.OrganizationsView)]
    [ProducesResponseType<OrganizationPageResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationPageResponse>> ListAsync([FromQuery] OrganizationListQuery query, CancellationToken cancellationToken) =>
        (await listOrganizations.ExecuteAsync(query, cancellationToken)).ToActionResult(OrganizationPageResponse.From);

    /// <summary>One organization.</summary>
    [HttpGet(UsersRoutes.IdTemplate)]
    [HasPermission(UsersPermissionKeys.OrganizationsView)]
    [ProducesResponseType<OrganizationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationResponse>> GetAsync(PublicId<OrganizationIds> id, CancellationToken cancellationToken) =>
        (await getOrganization.ExecuteAsync(id, cancellationToken)).ToActionResult(OrganizationResponse.From);

    /// <summary>Creates an active organization.</summary>
    [HttpPost]
    [HasPermission(UsersPermissionKeys.OrganizationsManage)]
    [ProducesResponseType<OrganizationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrganizationResponse>> CreateAsync([FromBody] CreateOrganizationCommand command, CancellationToken cancellationToken) =>
        (await createOrganization.ExecuteAsync(command, cancellationToken)).ToCreatedResult(OrganizationResponse.From);

    /// <summary>Changes an organization's code, names and kind.</summary>
    [HttpPut(UsersRoutes.IdTemplate)]
    [HasPermission(UsersPermissionKeys.OrganizationsManage)]
    [ProducesResponseType<OrganizationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrganizationResponse>> UpdateAsync(
        PublicId<OrganizationIds> id, [FromBody] UpdateOrganizationCommand command, CancellationToken cancellationToken) =>
        (await updateOrganization.ExecuteAsync(id, command, cancellationToken)).ToActionResult(OrganizationResponse.From);

    /// <summary>Deactivates an organization (its accounts stay; it accepts no new member).</summary>
    [HttpPost(UsersRoutes.Deactivate)]
    [HasPermission(UsersPermissionKeys.OrganizationsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeactivateAsync(PublicId<OrganizationIds> id, CancellationToken cancellationToken) =>
        (await setOrganizationActive.ExecuteAsync(id, isActive: false, cancellationToken)).ToNoContentResult();

    /// <summary>Reactivates an organization.</summary>
    [HttpPost(UsersRoutes.Reactivate)]
    [HasPermission(UsersPermissionKeys.OrganizationsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> ReactivateAsync(PublicId<OrganizationIds> id, CancellationToken cancellationToken) =>
        (await setOrganizationActive.ExecuteAsync(id, isActive: true, cancellationToken)).ToNoContentResult();
}

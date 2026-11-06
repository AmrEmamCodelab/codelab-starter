using Codelab.Security.PublicIds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Results;
using Starter.Modules.Lookups.Application;

namespace Starter.Modules.Lookups.Api;

/// <summary>The lookup read API for every signed-in user (dropdown sources). Thin: one use case, outcome mapped.</summary>
[ApiController]
[Route(LookupsRoutes.Lookups)]
[Produces(LookupsRoutes.JsonContentType)]
public sealed class LookupsController(GetActiveLookupValues getActiveLookupValues) : ControllerBase
{
    /// <summary>The active values of one lookup type, ordered by sort order (both names; the client picks the language).</summary>
    [HttpGet(LookupsRoutes.TypeTemplate)]
    [AuthenticatedUser]
    [ProducesResponseType<IReadOnlyList<LookupValueResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<LookupValueResponse>>> GetAsync(string type, CancellationToken cancellationToken) =>
        (await getActiveLookupValues.ExecuteAsync(type, cancellationToken)).ToActionResult(LookupValueResponse.ListFrom);
}

/// <summary>The admin lookups screen: view every type and value, activate and deactivate values. Thin.</summary>
[ApiController]
[Route(LookupsRoutes.AdminLookups)]
[Produces(LookupsRoutes.JsonContentType)]
public sealed class AdminLookupsController(
    ListLookupTypes listLookupTypes,
    ListLookupItems listLookupItems,
    SetLookupItemActive setLookupItemActive) : ControllerBase
{
    /// <summary>Every lookup type with its value count, ordered by key.</summary>
    [HttpGet]
    [HasPermission(LookupsPermissionKeys.View)]
    [ProducesResponseType<IReadOnlyList<LookupTypeResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LookupTypeResponse>>> ListTypesAsync(CancellationToken cancellationToken) =>
        (await listLookupTypes.ExecuteAsync(cancellationToken)).ToActionResult(LookupTypeResponse.ListFrom);

    /// <summary>Every value of one lookup type, active or not, ordered by sort order.</summary>
    [HttpGet(LookupsRoutes.TypeTemplate)]
    [HasPermission(LookupsPermissionKeys.View)]
    [ProducesResponseType<IReadOnlyList<LookupItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<LookupItemResponse>>> ListItemsAsync(string type, CancellationToken cancellationToken) =>
        (await listLookupItems.ExecuteAsync(type, cancellationToken)).ToActionResult(LookupItemResponse.ListFrom);

    /// <summary>Activates or deactivates one value (the only run-time change a lookup accepts).</summary>
    [HttpPut(LookupsRoutes.ItemActive)]
    [HasPermission(LookupsPermissionKeys.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SetActiveAsync(
        string type, PublicId<LookupItemIds> id, [FromBody] SetLookupItemActiveCommand command, CancellationToken cancellationToken) =>
        (await setLookupItemActive.ExecuteAsync(type, id, command, cancellationToken)).ToNoContentResult();
}

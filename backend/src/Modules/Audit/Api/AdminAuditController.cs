using Codelab.Security.PublicIds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Domain.Auditing;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Hosting;
using Starter.Infrastructure.Results;
using Starter.Modules.Audit.Application;

namespace Starter.Modules.Audit.Api;

/// <summary>The Audit routes, built from the kernel prefix (no route literal elsewhere).</summary>
public static class AuditRoutes
{
    /// <summary>The admin area: <c>api/admin</c>.</summary>
    public const string Admin = ApiRoutes.Prefix + ApiRoutes.Separator + "admin";

    /// <summary>The audit trail viewer: <c>api/admin/audit</c>.</summary>
    public const string Root = Admin + ApiRoutes.Separator + "audit";

    /// <summary>The response content type.</summary>
    public const string JsonContentType = "application/json";
}

/// <summary>The audit trail viewer. Thin: the action calls one use case and maps its outcome.</summary>
[ApiController]
[Route(AuditRoutes.Root)]
[Produces(AuditRoutes.JsonContentType)]
public sealed class AdminAuditController(ListAuditTrail listAuditTrail) : ControllerBase
{
    /// <summary>One page of the audit trail, newest first (values already masked).</summary>
    [HttpGet]
    [HasPermission(AuditPermissionKeys.View)]
    [ProducesResponseType<AuditPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuditPageResponse>> ListAsync([FromQuery] AuditTrailListQuery query, CancellationToken cancellationToken) =>
        (await listAuditTrail.ExecuteAsync(query, cancellationToken)).ToActionResult(AuditPageResponse.From);
}

/// <summary>One page of the audit trail.</summary>
/// <param name="Items">The entries, newest first.</param>
/// <param name="HasMore">True when older entries exist; ask again with <c>before</c> = the last item's id.</param>
public sealed record AuditPageResponse(IReadOnlyList<AuditEntryResponse> Items, bool HasMore)
{
    /// <summary>The response of <paramref name="page"/>.</summary>
    public static AuditPageResponse From(AuditEntryViewPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new AuditPageResponse([.. page.Items.Select(AuditEntryResponse.From)], page.HasMore);
    }
}

/// <summary>One audited change.</summary>
/// <param name="Id">The entry.</param>
/// <param name="EntityType">The audited entity type name.</param>
/// <param name="EntityId">The audited row, or null for a composite key.</param>
/// <param name="Action">Added = 1, Modified = 2, Deleted = 3.</param>
/// <param name="Actor">The acting subject id, or <c>SYSTEM</c>.</param>
/// <param name="At">When (UTC).</param>
/// <param name="Changes">The recorded values (masked where personal; ids are tokens).</param>
public sealed record AuditEntryResponse(
    PublicId<AuditEntryIds> Id,
    string EntityType,
    PublicId<AuditedEntityIds>? EntityId,
    AuditAction Action,
    string Actor,
    DateTimeOffset At,
    IReadOnlyList<AuditChangeResponse> Changes)
{
    /// <summary>The response of <paramref name="view"/>.</summary>
    public static AuditEntryResponse From(AuditEntryView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new AuditEntryResponse(
            view.Id,
            view.EntityType,
            view.EntityId,
            view.Action,
            view.Actor,
            view.At,
            [.. view.Changes.Select(change => new AuditChangeResponse(change.Property, change.OldValue, change.NewValue))]);
    }
}

/// <summary>One recorded value.</summary>
/// <param name="Property">The property name.</param>
/// <param name="OldValue">The value before, or null.</param>
/// <param name="NewValue">The value after, or null.</param>
public sealed record AuditChangeResponse(string Property, string? OldValue, string? NewValue);

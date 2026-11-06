using Codelab.Security.PublicIds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Results;
using Starter.Modules.Assignments.Application;
using Starter.Modules.Assignments.Application.Commands;
using Starter.Modules.Assignments.Application.Queues;

namespace Starter.Modules.Assignments.Api;

/// <summary>
/// The work queues of every record type: list, claim, release, assign. Thin: each action binds, is guarded by an
/// <see cref="AssignmentsPermissionKeys"/> key, calls one use case and maps its outcome. Every action also needs the
/// record type's queue permission, checked by the use case with its organization scope (403
/// <c>ASSIGNMENTS_QUEUE_FORBIDDEN</c>).
/// </summary>
[ApiController]
[Route(AssignmentsRoutes.Root)]
[Produces(AssignmentsRoutes.JsonContentType)]
public sealed class QueuesController(
    GetWorkQueuePage getWorkQueue,
    ClaimRecord claimRecord,
    ReleaseRecord releaseRecord,
    AssignRecord assignRecord) : ControllerBase
{
    /// <summary>One page of the queue in a view (<c>unassigned</c>, <c>mine</c>, <c>all</c>); 400, 403, 404.</summary>
    [HttpGet(AssignmentsRoutes.Queue)]
    [HasPermission(AssignmentsPermissionKeys.Work)]
    [ProducesResponseType<WorkQueueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkQueueResponse>> ListAsync(
        string recordType, [FromQuery] WorkQueueQuery query, CancellationToken cancellationToken) =>
        (await getWorkQueue.ExecuteAsync(query.ToRequest(recordType), cancellationToken)).ToActionResult(WorkQueueResponse.From);

    /// <summary>The caller takes the record: 204; 403, 404, 409 already claimed or not assignable.</summary>
    [HttpPost(AssignmentsRoutes.Claim)]
    [HasPermission(AssignmentsPermissionKeys.Work)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> ClaimAsync(string recordType, PublicId<QueueRecordIds> id, CancellationToken cancellationToken) =>
        (await claimRecord.ExecuteAsync(new QueueItemKey(recordType, id.Value), cancellationToken)).ToNoContentResult();

    /// <summary>The caller gives the record back: 204; 403 not the assignee, 404.</summary>
    [HttpPost(AssignmentsRoutes.Release)]
    [HasPermission(AssignmentsPermissionKeys.Work)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> ReleaseAsync(string recordType, PublicId<QueueRecordIds> id, CancellationToken cancellationToken) =>
        (await releaseRecord.ExecuteAsync(new QueueItemKey(recordType, id.Value), cancellationToken)).ToNoContentResult();

    /// <summary>Hands the record to <c>userId</c>: 204; 400 not eligible, 403, 404, 409.</summary>
    [HttpPost(AssignmentsRoutes.Assign)]
    [HasPermission(AssignmentsPermissionKeys.Assign)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> AssignAsync(
        string recordType, PublicId<QueueRecordIds> id, [FromBody] AssignRecordCommand command, CancellationToken cancellationToken) =>
        (await assignRecord.ExecuteAsync(new QueueItemKey(recordType, id.Value), command, cancellationToken)).ToNoContentResult();
}

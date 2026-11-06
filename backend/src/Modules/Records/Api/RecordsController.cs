using Codelab.Security.PublicIds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Results;
using Starter.Modules.Records.Application;

namespace Starter.Modules.Records.Api;

/// <summary>
/// The generic record API of every registered record type. The per-type permissions (view own, queue, the workflow's
/// action keys) come from the record type's registration and catalog, so these endpoints are open to any signed-in user
/// (<see cref="AuthenticatedUserAttribute"/>, allowlisted) and the use case checks them live; a record the caller may not
/// see answers 404. The oversight list carries <see cref="RecordsPermissionKeys.OversightView"/>. Thin: one use case per
/// action, outcomes mapped by OutcomeResults.
/// </summary>
[ApiController]
[Route(RecordsRoutes.RecordType)]
[Produces(RecordsRoutes.JsonContentType)]
public sealed class RecordsController(
    ListMyRecords listMyRecords,
    ListRecordOversight listRecordOversight,
    GetRecordDetail getRecordDetail,
    GetRecordTimeline getRecordTimeline,
    TakeRecordTransition takeRecordTransition) : ControllerBase
{
    /// <summary>The caller's own records of the type, newest first (drafts included); 403 without the view-own permission, 404 for an unknown type.</summary>
    [HttpGet(RecordsRoutes.Mine)]
    [AuthenticatedUser]
    [ProducesResponseType<RecordPageResponse<RecordListItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecordPageResponse<RecordListItemResponse>>> MineAsync(
        string recordType, [FromQuery] RecordListParameters parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var query = new MyRecordsQuery(recordType, parameters.Status, Page: parameters.Page, PageSize: parameters.PageSize);
        return (await listMyRecords.ExecuteAsync(query, cancellationToken)).ToActionResult(RecordListItemResponse.PageFrom);
    }

    /// <summary>Every submitted record of the type with its owner, read-only (no allowed actions); 400 for a number filter too long.</summary>
    [HttpGet(RecordsRoutes.Oversight)]
    [HasPermission(RecordsPermissionKeys.OversightView)]
    [ProducesResponseType<RecordPageResponse<RecordListItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecordPageResponse<RecordListItemResponse>>> OversightAsync(
        string recordType, [FromQuery] RecordListParameters parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var query = new OversightQuery(recordType, parameters.Status, parameters.Number, Page: parameters.Page, PageSize: parameters.PageSize);
        return (await listRecordOversight.ExecuteAsync(query, cancellationToken)).ToActionResult(RecordListItemResponse.PageFrom);
    }

    /// <summary>One record for the caller's audience with the actions the caller may take now; 404 when missing or hidden.</summary>
    [HttpGet(RecordsRoutes.IdTemplate)]
    [AuthenticatedUser]
    [ProducesResponseType<RecordDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecordDetailResponse>> GetAsync(string recordType, PublicId<RecordsIds> id, CancellationToken cancellationToken) =>
        (await getRecordDetail.ExecuteAsync(recordType, id.Value, cancellationToken)).ToActionResult(RecordDetailResponse.From);

    /// <summary>The record's timeline, oldest first; 404 when missing or hidden.</summary>
    [HttpGet(RecordsRoutes.Timeline)]
    [AuthenticatedUser]
    [ProducesResponseType<IReadOnlyList<TimelineEntryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<TimelineEntryResponse>>> TimelineAsync(
        string recordType, PublicId<RecordsIds> id, CancellationToken cancellationToken) =>
        (await getRecordTimeline.ExecuteAsync(recordType, id.Value, cancellationToken)).ToActionResult(TimelineEntryResponse.ListFrom);

    /// <summary>
    /// Takes one action and answers the record's new detail: 400 (reason required or too long, or a submit that fails
    /// validation), 403 (not the caller's action), 404 (missing, hidden or unknown action), 409 (not allowed from the
    /// current status, required attachments missing, a concurrent change).
    /// </summary>
    [HttpPost(RecordsRoutes.Transitions)]
    [AuthenticatedUser]
    [ProducesResponseType<RecordDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RecordDetailResponse>> TransitionAsync(
        string recordType, PublicId<RecordsIds> id, [FromBody] TransitionCommand command, CancellationToken cancellationToken) =>
        (await takeRecordTransition.ExecuteAsync(recordType, id.Value, command, cancellationToken)).ToActionResult(RecordDetailResponse.From);
}

using Codelab.Security.PublicIds;
using Codelab.Security.Signing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Application.Results;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Results;
using Starter.Modules.Attachments.Application;

namespace Starter.Modules.Attachments.Api;

/// <summary>Why the download is exempt from response-body signing (one owner of the reason).</summary>
public static class AttachmentsSigning
{
    /// <summary>
    /// The reason: a file download is streamed straight from the attachment service and may be large; hashing it would
    /// buffer the whole file. The signature still covers the status, the content type and the request binding.
    /// </summary>
    public const string FileDownloadReason =
        "A file download streamed from the attachment service: hashing the body would buffer the whole file. The "
        + "response signature still covers the status, the content type and the request binding.";
}

/// <summary>
/// The attachment catalog and single attachments: the types of a record type, the Development demo record, removal.
/// Thin: each action calls one use case and maps its outcome.
/// </summary>
[ApiController]
[Route(AttachmentsRoutes.Root)]
[Produces(AttachmentsRoutes.JsonContentType)]
public sealed class AttachmentsController(
    ListAttachmentTypes listAttachmentTypes,
    RemoveAttachment removeAttachment,
    AttachableRecords attachableRecords) : ControllerBase
{
    /// <summary>The active attachment types of one record type, ordered by key; 404 for an unknown record type.</summary>
    [HttpGet(AttachmentsRoutes.Types)]
    [HasPermission(AttachmentsPermissionKeys.View)]
    [ProducesResponseType<IReadOnlyList<AttachmentTypeResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AttachmentTypeResponse>>> ListTypesAsync(
        [FromQuery(Name = AttachmentsRoutes.RecordTypeQuery)] string? recordType, CancellationToken cancellationToken) =>
        (await listAttachmentTypes.ExecuteAsync(recordType, cancellationToken)).ToActionResult(AttachmentTypeResponse.ListFrom);

    /// <summary>
    /// Removes an attachment (soft; the bytes are deleted later through the outbox): 204, or 404 for a missing or
    /// removed attachment or a record the user may not change.
    /// </summary>
    [HttpDelete(AttachmentsRoutes.IdTemplate)]
    [HasPermission(AttachmentsPermissionKeys.Remove)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemoveAsync(PublicId<AttachmentIds> id, CancellationToken cancellationToken) =>
        (await removeAttachment.ExecuteAsync(id.Value, cancellationToken)).ToNoContentResult();

    /// <summary>
    /// The Development demo record the attachments demo page works on; 404 <c>ATTACHMENTS_RECORD_TYPE_UNKNOWN</c> when
    /// the demo record type is not registered (every environment but Development).
    /// </summary>
    [HttpGet(AttachmentsRoutes.DemoRecord)]
    [HasPermission(AttachmentsPermissionKeys.View)]
    [ProducesResponseType<DemoRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<DemoRecordResponse> GetDemoRecord() =>
        attachableRecords.IsKnown(DemoRecord.RecordType)
            ? new OkObjectResult(DemoRecordResponse.Demo)
            : Refusal.NotFound(AttachmentsErrorCodes.RecordTypeUnknown).ToActionResult();
}

/// <summary>
/// The attachments of one record: list, upload ticket, link by receipt. The record id is a PublicId of family
/// <c>attachable-records</c>; the record's own access decides per record. Thin: one use case per action.
/// </summary>
[ApiController]
[Route(AttachmentsRoutes.RecordAttachments)]
[Produces(AttachmentsRoutes.JsonContentType)]
public sealed class RecordAttachmentsController(
    ListRecordAttachments listRecordAttachments,
    IssueUploadTicket issueUploadTicket,
    LinkUploadedFile linkUploadedFile) : ControllerBase
{
    /// <summary>The record's live attachments and the required types it still misses; 404 for an unknown or hidden record.</summary>
    [HttpGet]
    [HasPermission(AttachmentsPermissionKeys.View)]
    [ProducesResponseType<RecordAttachmentsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecordAttachmentsResponse>> ListAsync(
        string recordType, PublicId<AttachableRecordIds> recordId, CancellationToken cancellationToken) =>
        (await listRecordAttachments.ExecuteAsync(Record(recordType, recordId), cancellationToken)).ToActionResult(RecordAttachmentsResponse.From);

    /// <summary>
    /// A sealed, single-use ticket to upload files of one type straight to the attachment service; 400, 404, or 409
    /// when the record already holds the type's most files.
    /// </summary>
    [HttpPost(AttachmentsRoutes.UploadTicket)]
    [HasPermission(AttachmentsPermissionKeys.Upload)]
    [ProducesResponseType<UploadTicketResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UploadTicketResponse>> IssueTicketAsync(
        string recordType, PublicId<AttachableRecordIds> recordId, [FromBody] IssueUploadTicketCommand command, CancellationToken cancellationToken) =>
        (await issueUploadTicket.ExecuteAsync(Record(recordType, recordId), command, cancellationToken)).ToActionResult(UploadTicketResponse.From);

    /// <summary>
    /// Links an uploaded file to the record by its receipt (201 with the attachment); 400 for an invalid, tampered or
    /// foreign receipt or an extension the type refuses, 404, or 409 when the type is full.
    /// </summary>
    [HttpPost]
    [HasPermission(AttachmentsPermissionKeys.Upload)]
    [ProducesResponseType<RecordAttachmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RecordAttachmentResponse>> LinkAsync(
        string recordType, PublicId<AttachableRecordIds> recordId, [FromBody] LinkUploadedFileCommand command, CancellationToken cancellationToken) =>
        (await linkUploadedFile.ExecuteAsync(Record(recordType, recordId), command, cancellationToken)).ToCreatedResult(RecordAttachmentResponse.From);

    private static AttachableRecord Record(string recordType, PublicId<AttachableRecordIds> recordId) => new(recordType, recordId.Value);
}

/// <summary>
/// The download: the bytes of one attachment, streamed from the attachment service over the service assertion, with
/// the name and type this database recorded. The response body is not signed (<see cref="AttachmentsSigning"/>).
/// </summary>
[ApiController]
[Route(AttachmentsRoutes.Root)]
public sealed class AttachmentContentController(DownloadAttachment downloadAttachment) : ControllerBase
{
    /// <summary>
    /// The file (no <c>Content-Disposition</c>: the SPA names the saved Blob; nosniff, sandboxed); 404 for a missing or removed attachment, a record
    /// the user may not view, or bytes the service no longer has; 503 when the service is unreachable.
    /// </summary>
    [HttpGet(AttachmentsRoutes.Content)]
    [HasPermission(AttachmentsPermissionKeys.View)]
    [UnsignedResponse(AttachmentsSigning.FileDownloadReason)]
    [ProducesResponseType<FileResult>(StatusCodes.Status200OK, AttachmentsRoutes.FileContentType)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> DownloadAsync(PublicId<AttachmentIds> id, CancellationToken cancellationToken) =>
        (await downloadAttachment.ExecuteAsync(id.Value, cancellationToken)).ToFileResult();
}

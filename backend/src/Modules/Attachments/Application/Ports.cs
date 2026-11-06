using Codelab.Errors;
using Starter.Application.Persistence;
using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Application;

/// <summary>
/// The attachment type catalog and the record attachments (implemented by the module's EF Core context in
/// Infrastructure). It is also the module's unit of work: <see cref="IUnitOfWork.SaveChangesAsync"/> writes what
/// <see cref="Add"/> and the tracked entities staged.
/// </summary>
public interface IAttachmentStore : IUnitOfWork
{
    /// <summary>Every attachment type of <paramref name="recordType"/>, active or retired, ordered by key. Read-only.</summary>
    Task<IReadOnlyList<ServiceAttachment>> ListTypesAsync(string recordType, CancellationToken cancellationToken);

    /// <summary>The attachment type <paramref name="typeKey"/> of <paramref name="recordType"/>, active or retired, or null. Read-only.</summary>
    Task<ServiceAttachment?> FindTypeAsync(string recordType, string typeKey, CancellationToken cancellationToken);

    /// <summary>The live (not removed) attachments of one record, ordered by upload time, then id. Read-only.</summary>
    Task<IReadOnlyList<RecordAttachment>> ListLiveAsync(string recordType, long recordId, CancellationToken cancellationToken);

    /// <summary>The attachment <paramref name="attachmentId"/>, live or removed, tracked for a change; or null.</summary>
    Task<RecordAttachment?> FindAsync(long attachmentId, CancellationToken cancellationToken);

    /// <summary>The attachment that links file <paramref name="fileId"/>, live or removed, or null (a file id is linked at most once).</summary>
    Task<RecordAttachment?> FindByFileIdAsync(string fileId, CancellationToken cancellationToken);

    /// <summary>Stages a new attachment for the next save.</summary>
    void Add(RecordAttachment attachment);
}

/// <summary>
/// Verifies an upload receipt the attachment service issued (an <c>r1.</c> token from
/// <c>Codelab.Security.Uploads.UploadReceiptSigner</c>, checked offline under the shared receipt key ring). Receipts
/// are mandatory: with receipts disabled nothing verifies, so nothing can be linked.
/// </summary>
public interface IAttachmentReceiptVerifier
{
    /// <summary>
    /// What the receipt attests, or null when it is malformed, its MAC does not verify under a ring key, or its file
    /// extension has no MIME type in <c>Codelab.Security.Uploads.UploadContentTypes</c>. The scope is NOT checked here:
    /// the caller compares it with the scope it expects.
    /// </summary>
    VerifiedUpload? Verify(string receipt);
}

/// <summary>A verified upload receipt.</summary>
/// <param name="Scope">The scope the receipt was issued for (the upload ticket's subject, see <see cref="UploadScopes"/>).</param>
/// <param name="File">The stored file; <see cref="StoredFile.ContentType"/> comes from the extension, never from the client.</param>
public sealed record VerifiedUpload(string Scope, StoredFile File);

/// <summary>
/// The attachment service's file API, called server to server with the ES256 service assertion (the browser never
/// reads or deletes there). An outage is thrown as <see cref="AttachmentStorageUnavailableException"/>.
/// </summary>
public interface IAttachmentStorageClient
{
    /// <summary>The bytes of <paramref name="fileId"/> as a stream the caller disposes, or null when the service has no such file.</summary>
    /// <exception cref="AttachmentStorageUnavailableException">The service could not answer.</exception>
    Task<Stream?> OpenReadAsync(string fileId, CancellationToken cancellationToken);

    /// <summary>Deletes <paramref name="fileId"/>; false when the service had no such file (a repeated delete is not an error).</summary>
    /// <exception cref="AttachmentStorageUnavailableException">The service could not answer.</exception>
    Task<bool> DeleteAsync(string fileId, CancellationToken cancellationToken);
}

/// <summary>
/// Schedules the deletion of a removed attachment's bytes on the attachment service: an outbox message of type
/// <see cref="AttachmentsOutboxMessageTypes.DeleteFile"/> whose handler calls <see cref="IAttachmentStorageClient.DeleteAsync"/>
/// (retried with back-off while the service is down; idempotent).
/// </summary>
public interface IAttachmentFileCleanup
{
    /// <summary>Enqueues the deletion of <paramref name="fileId"/>. Call it after the removal was saved.</summary>
    Task EnqueueDeleteAsync(string fileId, CancellationToken cancellationToken);
}

/// <summary>The Attachments module's outbox message types.</summary>
public static class AttachmentsOutboxMessageTypes
{
    /// <summary>Delete one file on the attachment service; the payload is a <see cref="DeleteAttachmentFilePayload"/>.</summary>
    public const string DeleteFile = "attachments.delete-file";
}

/// <summary>The payload of an <see cref="AttachmentsOutboxMessageTypes.DeleteFile"/> message.</summary>
/// <param name="FileId">The attachment-service file id to delete.</param>
public sealed record DeleteAttachmentFilePayload(string FileId);

/// <summary>
/// Issues the short-lived, single-use sealed ticket (purpose <see cref="UploadTickets.Purpose"/>) that lets the browser
/// upload straight to the attachment service, with the ticket header name and the upload URL it needs.
/// </summary>
public interface IUploadTicketIssuer
{
    /// <summary>A ticket whose subject is <paramref name="scope"/> (from <see cref="UploadScopes.For"/>); the service echoes it as the receipt scope.</summary>
    IssuedUploadTicket Issue(string scope);
}

/// <summary>An issued upload ticket.</summary>
/// <param name="Ticket">The sealed ticket token.</param>
/// <param name="TicketHeader">The request header that carries it (<c>X-{Prefix}-Upload-Ticket</c>).</param>
/// <param name="UploadUrl">The absolute <c>POST /api/uploads</c> URL of the attachment service.</param>
/// <param name="ExpiresAt">When the ticket stops opening.</param>
public sealed record IssuedUploadTicket(string Ticket, string TicketHeader, string UploadUrl, DateTimeOffset ExpiresAt);

/// <summary>The upload ticket constants shared with the attachment service.</summary>
public static class UploadTickets
{
    /// <summary>The sealed-ticket purpose of an upload ticket (the service opens tickets for this purpose only).</summary>
    public const string Purpose = "attachment-upload";
}

/// <summary>
/// The attachment service could not answer (connection failure, timeout, a 5xx). Thrown by the storage client adapter
/// around the outbound call only; the module's failure classifier answers 503
/// <see cref="AttachmentsErrorCodes.StorageUnavailable"/> ahead of the generic 502.
/// </summary>
public sealed class AttachmentStorageUnavailableException : UpstreamUnavailableException
{
    /// <summary>The upstream name written to the log.</summary>
    public const string UpstreamName = "attachment-service";

    /// <summary>Creates the exception around the failed call's <paramref name="innerException"/>.</summary>
    public AttachmentStorageUnavailableException(Exception? innerException)
        : base(UpstreamName, innerException)
    {
    }
}

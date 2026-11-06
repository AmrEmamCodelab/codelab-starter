using Starter.Application.Results;

namespace Starter.Modules.Attachments.Application;

/// <summary>
/// <c>DELETE /api/attachments/{id}</c>: removes a live attachment (soft: the row keeps who and when) once the record
/// allows a change, then enqueues the deletion of its bytes on the attachment service
/// (<see cref="IAttachmentFileCleanup"/>, after the save).
/// </summary>
public sealed class RemoveAttachment(AttachableRecords records, IAttachmentStore store, IAttachmentFileCleanup cleanup, AttachmentActor actor)
{
    /// <summary>
    /// Done, or <see cref="AttachmentsErrorCodes.NotFound"/> (404) for a missing or already removed attachment or a
    /// record the user may not change.
    /// </summary>
    public async Task<Outcome<Done>> ExecuteAsync(long attachmentId, CancellationToken cancellationToken)
    {
        if (await store.FindAsync(attachmentId, cancellationToken) is not { IsLive: true } attachment)
        {
            return Refusal.NotFound(AttachmentsErrorCodes.NotFound);
        }

        var record = new AttachableRecord(attachment.RecordType, attachment.RecordId);
        if (await records.RefuseAsync(record, RecordAccessKind.Change, cancellationToken) is not null)
        {
            return Refusal.NotFound(AttachmentsErrorCodes.NotFound);
        }

        _ = attachment.Remove(actor.Stamp());
        await store.SaveChangesAsync(cancellationToken);
        await cleanup.EnqueueDeleteAsync(attachment.FileId, cancellationToken);
        return Done.Value;
    }
}

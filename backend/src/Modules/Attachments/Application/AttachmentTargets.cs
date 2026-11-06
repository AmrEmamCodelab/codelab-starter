using Starter.Application.Results;
using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Application;

/// <summary>A record and one of its active attachment types, ready for a ticket or a link.</summary>
/// <param name="Record">The record (record type known, access to change granted).</param>
/// <param name="Type">The active attachment type.</param>
/// <param name="LiveOfType">The record's live attachments of that type.</param>
public sealed record AttachmentTarget(AttachableRecord Record, ServiceAttachment Type, IReadOnlyList<RecordAttachment> LiveOfType)
{
    /// <summary>Where a new attachment of this target belongs.</summary>
    public AttachmentOwner Owner => new(Record.RecordType!, Record.RecordId, Type.Id);

    /// <summary>True when the record may take one more file of the type.</summary>
    public bool HasRoom => Type.HasRoomFor(LiveOfType.Count);

    /// <summary>How many more files of the type the record may take.</summary>
    public int RemainingSlots => Type.RemainingSlots(LiveOfType.Count);
}

/// <summary>
/// Resolves the target of an upload (shared by the ticket and the link, so both refuse the same way): the record type
/// is known, the user may change the record, and the type is active for that record type.
/// </summary>
public sealed class AttachmentTargets(AttachableRecords records, IAttachmentStore store)
{
    /// <summary>
    /// The target, or a refusal: <see cref="AttachmentsErrorCodes.RecordTypeUnknown"/>,
    /// <see cref="AttachmentsErrorCodes.NotFound"/> (record missing or not changeable by the user), or
    /// <see cref="AttachmentsErrorCodes.TypeNotFound"/> (no such type for the record type, or retired); all 404.
    /// </summary>
    public async Task<Outcome<AttachmentTarget>> ResolveForChangeAsync(AttachableRecord record, string typeKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (await records.RefuseAsync(record, RecordAccessKind.Change, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var type = await store.FindTypeAsync(record.RecordType!, typeKey, cancellationToken);
        if (type is not { IsActive: true })
        {
            return Refusal.NotFound(AttachmentsErrorCodes.TypeNotFound);
        }

        var live = await store.ListLiveAsync(record.RecordType!, record.RecordId, cancellationToken);
        return new AttachmentTarget(record, type, [.. live.Where(attachment => attachment.ServiceAttachmentId == type.Id)]);
    }
}

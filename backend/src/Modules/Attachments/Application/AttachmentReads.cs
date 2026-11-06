using Starter.Application.Attachments;
using Starter.Application.Results;
using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Application;

/// <summary><c>GET /api/attachments/types?recordType={key}</c>: the active attachment types of one record type.</summary>
public sealed class ListAttachmentTypes(AttachableRecords records, IAttachmentStore store)
{
    /// <summary>The active types ordered by key, or <see cref="AttachmentsErrorCodes.RecordTypeUnknown"/> (404).</summary>
    public async Task<Outcome<IReadOnlyList<AttachmentTypeView>>> ExecuteAsync(string? recordType, CancellationToken cancellationToken)
    {
        if (!records.IsKnown(recordType))
        {
            return Refusal.NotFound(AttachmentsErrorCodes.RecordTypeUnknown);
        }

        var types = await store.ListTypesAsync(recordType!, cancellationToken);
        IReadOnlyList<AttachmentTypeView> views = [.. types.Where(type => type.IsActive).Select(AttachmentTypeView.From)];
        return Outcome.Success(views);
    }
}

/// <summary>
/// <c>GET /api/records/{recordType}/{recordId}/attachments</c>: a record's live attachments and the required types it
/// still misses (the same rule as the <see cref="IRequiredAttachments"/> interlock).
/// </summary>
public sealed class ListRecordAttachments(AttachableRecords records, IAttachmentStore store)
{
    /// <summary>
    /// The attachments, or a refusal (<see cref="AttachmentsErrorCodes.RecordTypeUnknown"/>,
    /// <see cref="AttachmentsErrorCodes.NotFound"/>).
    /// </summary>
    public async Task<Outcome<RecordAttachmentsView>> ExecuteAsync(AttachableRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (await records.RefuseAsync(record, RecordAccessKind.View, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var types = await store.ListTypesAsync(record.RecordType!, cancellationToken);
        var live = await store.ListLiveAsync(record.RecordType!, record.RecordId, cancellationToken);
        var keyById = types.ToDictionary(type => type.Id, type => type.Key);
        IReadOnlyList<RecordAttachmentView> items = [.. live.Select(attachment => RecordAttachmentView.From(attachment, keyById[attachment.ServiceAttachmentId]))];
        return new RecordAttachmentsView(items, RequiredAttachmentRule.MissingTypeKeys(types, live));
    }
}

/// <summary>
/// <c>GET /api/attachments/{id}/content</c>: streams a live attachment from the attachment service, after the same
/// view check as the list (so nothing is listed that the download would refuse).
/// </summary>
public sealed class DownloadAttachment(AttachableRecords records, IAttachmentStore store, IAttachmentStorageClient storage)
{
    /// <summary>
    /// The bytes with the recorded name and type, or <see cref="AttachmentsErrorCodes.NotFound"/> (404) for a missing or
    /// removed attachment, a record the user may not view, or bytes the service no longer has.
    /// </summary>
    /// <exception cref="AttachmentStorageUnavailableException">The attachment service could not answer.</exception>
    public async Task<Outcome<AttachmentDownload>> ExecuteAsync(long attachmentId, CancellationToken cancellationToken)
    {
        if (await store.FindAsync(attachmentId, cancellationToken) is not { IsLive: true } attachment)
        {
            return Refusal.NotFound(AttachmentsErrorCodes.NotFound);
        }

        var record = new AttachableRecord(attachment.RecordType, attachment.RecordId);
        if (await records.RefuseAsync(record, RecordAccessKind.View, cancellationToken) is not null)
        {
            return Refusal.NotFound(AttachmentsErrorCodes.NotFound);
        }

        var content = await storage.OpenReadAsync(attachment.FileId, cancellationToken);
        return content is null
            ? Refusal.NotFound(AttachmentsErrorCodes.NotFound)
            : new AttachmentDownload(content, attachment.FileName, attachment.ContentType, attachment.SizeBytes);
    }
}

/// <summary>
/// The kernel <see cref="IRequiredAttachments"/> interlock over the same rule as the record list
/// (<see cref="RequiredAttachmentRule"/>): the module that owns a record calls it before submit and refuses with
/// <see cref="AttachmentErrorCodes.RequiredMissing"/> when the answer is not empty.
/// </summary>
public sealed class RequiredAttachmentsCheck(IAttachmentStore store) : IRequiredAttachments
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> FindMissingTypeKeysAsync(string recordType, long recordId, CancellationToken cancellationToken)
    {
        if (!AttachmentsLimits.IsRecordType(recordType) || recordId < 1)
        {
            return [];
        }

        var types = await store.ListTypesAsync(recordType, cancellationToken);
        if (!types.Any(type => type.IsEnforcedAsRequired))
        {
            return [];
        }

        var live = await store.ListLiveAsync(recordType, recordId, cancellationToken);
        return RequiredAttachmentRule.MissingTypeKeys(types, live);
    }
}

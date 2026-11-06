using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Application;

/// <summary>
/// <c>POST /api/records/{recordType}/{recordId}/attachments</c>: links a file the browser uploaded to the attachment
/// service. Only what the verified receipt attests is stored (id, name, size, SHA-256; the MIME type comes from the
/// extension), never a client-sent value. The receipt's scope must be exactly this record, type and user. Idempotent
/// per file id: linking the same receipt again answers the existing attachment.
/// </summary>
public sealed class LinkUploadedFile(
    IValidator<LinkUploadedFileCommand> validator,
    AttachmentTargets targets,
    ScopedReceipts receipts,
    IAttachmentStore store,
    AttachmentActor actor)
{
    /// <summary>
    /// The linked attachment, or a refusal: <see cref="PlatformErrorCodes.Validation"/>; the target refusals of
    /// <see cref="AttachmentTargets"/>; <see cref="AttachmentsErrorCodes.ReceiptInvalid"/> (400, a forged, tampered or
    /// foreign-scope receipt, or a file linked elsewhere or removed); <see cref="AttachmentsErrorCodes.ExtensionNotAllowedForType"/>
    /// (400); <see cref="AttachmentsErrorCodes.MaxFilesReached"/> (409).
    /// </summary>
    public async Task<Outcome<RecordAttachmentView>> ExecuteAsync(AttachableRecord record, LinkUploadedFileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        var resolved = await targets.ResolveForChangeAsync(record, command.TypeKey!, cancellationToken);
        if (!resolved.IsSuccess)
        {
            return resolved.Refusal;
        }

        var target = resolved.Value;
        var uploaded = actor.Stamp();
        if (receipts.VerifiedFile(command.Receipt!, target, uploaded.Subject) is not { } file)
        {
            return Refusal.Invalid(AttachmentsErrorCodes.ReceiptInvalid);
        }

        if (await store.FindByFileIdAsync(file.FileId, cancellationToken) is { } existing)
        {
            return existing.IsLive && existing.BelongsTo(target.Owner)
                ? RecordAttachmentView.From(existing, target.Type.Key)
                : Refusal.Invalid(AttachmentsErrorCodes.ReceiptInvalid);
        }

        return await LinkNewAsync(target, file, uploaded, cancellationToken);
    }

    private async Task<Outcome<RecordAttachmentView>> LinkNewAsync(
        AttachmentTarget target, StoredFile file, AttachmentStamp uploaded, CancellationToken cancellationToken)
    {
        if (!target.Type.AllowsFile(file.FileName))
        {
            return Refusal.Invalid(AttachmentsErrorCodes.ExtensionNotAllowedForType);
        }

        if (!target.HasRoom)
        {
            return Refusal.Conflict(AttachmentsErrorCodes.MaxFilesReached);
        }

        var attachment = RecordAttachment.Link(target.Owner, file, uploaded);
        store.Add(attachment);
        await store.SaveChangesAsync(cancellationToken);
        return RecordAttachmentView.From(attachment, target.Type.Key);
    }
}

/// <summary>The receipt check of a link: the signature (<see cref="IAttachmentReceiptVerifier"/>) and the scope (<see cref="UploadScopes"/>).</summary>
public sealed class ScopedReceipts(IAttachmentReceiptVerifier receipts, UploadScopes scopes)
{
    /// <summary>The receipt's file when the receipt verifies, is well formed and was issued for exactly this target and user; otherwise null.</summary>
    public StoredFile? VerifiedFile(string receipt, AttachmentTarget target, Guid userSubject)
    {
        ArgumentNullException.ThrowIfNull(target);
        var verified = receipts.Verify(receipt);
        return verified is not null && verified.File.IsWellFormed() && scopes.Matches(verified.Scope, target.Owner, target.Type.Key, userSubject)
            ? verified.File
            : null;
    }
}

using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Application;

/// <summary>The body of <c>POST /api/records/{recordType}/{recordId}/attachments/upload-ticket</c>.</summary>
/// <param name="TypeKey">The attachment type the upload is for.</param>
public sealed record IssueUploadTicketCommand(string? TypeKey);

/// <summary>Validates <see cref="IssueUploadTicketCommand"/>: a well-formed type key.</summary>
public sealed class IssueUploadTicketCommandValidator : IValidator<IssueUploadTicketCommand>
{
    /// <inheritdoc />
    public ValidationErrors Validate(IssueUploadTicketCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return AttachmentFields.RequireTypeKey(new ValidationErrors(), input.TypeKey);
    }
}

/// <summary>The body of <c>POST /api/records/{recordType}/{recordId}/attachments</c>.</summary>
/// <param name="TypeKey">The attachment type the file was uploaded for.</param>
/// <param name="Receipt">The attachment service's receipt for the stored file.</param>
public sealed record LinkUploadedFileCommand(string? TypeKey, string? Receipt);

/// <summary>Validates <see cref="LinkUploadedFileCommand"/>: a well-formed type key and a receipt within <see cref="AttachmentsLimits.ReceiptMaxLength"/>.</summary>
public sealed class LinkUploadedFileCommandValidator : IValidator<LinkUploadedFileCommand>
{
    /// <inheritdoc />
    public ValidationErrors Validate(LinkUploadedFileCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = AttachmentFields.RequireTypeKey(new ValidationErrors(), input.TypeKey);
        return errors.RequireText(FieldNames.Of(nameof(LinkUploadedFileCommand.Receipt)), input.Receipt, AttachmentsLimits.ReceiptMaxLength);
    }
}

/// <summary>The field rules the upload commands share.</summary>
internal static class AttachmentFields
{
    /// <summary>Adds Required for a blank type key and InvalidFormat for one that is not kebab-case.</summary>
    public static ValidationErrors RequireTypeKey(ValidationErrors errors, string? typeKey)
    {
        var field = FieldNames.Of(nameof(IssueUploadTicketCommand.TypeKey));
        if (string.IsNullOrWhiteSpace(typeKey))
        {
            return errors.Add(field, ValidationMessages.Required);
        }

        return AttachmentsLimits.IsTypeKey(typeKey) ? errors : errors.Add(field, ValidationMessages.InvalidFormat);
    }
}

/// <summary>
/// <c>POST /api/records/{recordType}/{recordId}/attachments/upload-ticket</c>: a sealed, single-use ticket for one
/// upload to the attachment service, scoped to this record, type and user (<see cref="UploadScopes"/>). Refused when the
/// record may not change, the type is unknown or retired, or the record already holds the type's most files.
/// </summary>
public sealed class IssueUploadTicket(
    IValidator<IssueUploadTicketCommand> validator,
    AttachmentTargets targets,
    IUploadTicketIssuer issuer,
    UploadScopes scopes,
    AttachmentActor actor)
{
    /// <summary>
    /// The ticket, or a refusal (<see cref="PlatformErrorCodes.Validation"/>, <see cref="AttachmentsErrorCodes.RecordTypeUnknown"/>,
    /// <see cref="AttachmentsErrorCodes.NotFound"/>, <see cref="AttachmentsErrorCodes.TypeNotFound"/>,
    /// <see cref="AttachmentsErrorCodes.MaxFilesReached"/>).
    /// </summary>
    public async Task<Outcome<UploadTicketView>> ExecuteAsync(AttachableRecord record, IssueUploadTicketCommand command, CancellationToken cancellationToken)
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
        if (!target.HasRoom)
        {
            return Refusal.Conflict(AttachmentsErrorCodes.MaxFilesReached);
        }

        var issued = issuer.Issue(scopes.For(target.Owner, target.Type.Key, actor.RequireSubject()));
        return new UploadTicketView(issued.Ticket, issued.TicketHeader, issued.UploadUrl, issued.ExpiresAt, target.Type.AllowedExtensions, target.RemainingSlots);
    }
}

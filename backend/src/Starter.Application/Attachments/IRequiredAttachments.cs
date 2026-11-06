namespace Starter.Application.Attachments;

/// <summary>
/// The required-attachments interlock (implemented by the Attachments module): a record cannot move on (for example
/// submit) while an active, required attachment type of its record type has no attachment. The caller refuses with
/// <see cref="AttachmentErrorCodes.RequiredMissing"/> (409) when the list is not empty.
/// </summary>
public interface IRequiredAttachments
{
    /// <summary>
    /// The keys of the active, required attachment types of <paramref name="recordType"/> that record
    /// <paramref name="recordId"/> has no live attachment of, ordered by key; empty when nothing is missing or the
    /// record type has no attachment types. It does not check the caller's access to the record (the caller owns that).
    /// </summary>
    Task<IReadOnlyList<string>> FindMissingTypeKeysAsync(string recordType, long recordId, CancellationToken cancellationToken);
}

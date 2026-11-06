using Starter.Application.Results;

namespace Starter.Application.Attachments;

/// <summary>
/// The one way an owning module applies the <see cref="IRequiredAttachments"/> interlock before a record moves on (for
/// example submit): the 409 <see cref="AttachmentErrorCodes.RequiredMissing"/> refusal while a required type is
/// missing, otherwise null and the action goes ahead. The caller checks its own access to the record first.
/// </summary>
public static class RequiredAttachmentsGate
{
    /// <summary>
    /// The refusal when record <paramref name="recordId"/> of <paramref name="recordType"/> still misses a required
    /// attachment type; null when nothing is missing.
    /// </summary>
    public static async Task<Refusal?> RefuseWhileMissingAsync(
        this IRequiredAttachments interlock, string recordType, long recordId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(interlock);
        var missing = await interlock.FindMissingTypeKeysAsync(recordType, recordId, cancellationToken);
        return missing.Count == 0 ? null : Refusal.Conflict(AttachmentErrorCodes.RequiredMissing);
    }
}

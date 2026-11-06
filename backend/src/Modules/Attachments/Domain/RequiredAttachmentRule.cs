namespace Starter.Modules.Attachments.Domain;

/// <summary>
/// The one owner of "which required attachments are missing": the active, required types of a record type minus the
/// types the record holds at least one live attachment of. Used by the record attachment list and by the interlock
/// (<c>IRequiredAttachments</c>), so the screen and the submit check can never disagree.
/// </summary>
public static class RequiredAttachmentRule
{
    /// <summary>
    /// The keys of the types in <paramref name="types"/> that are enforced as required
    /// (<see cref="ServiceAttachment.IsEnforcedAsRequired"/>) and have no live attachment in
    /// <paramref name="attachments"/>, ordered by key (ordinal).
    /// </summary>
    public static IReadOnlyList<string> MissingTypeKeys(IEnumerable<ServiceAttachment> types, IEnumerable<RecordAttachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(attachments);
        var covered = attachments.Where(attachment => attachment.IsLive).Select(attachment => attachment.ServiceAttachmentId).ToHashSet();
        return [.. types
            .Where(type => type.IsEnforcedAsRequired && !covered.Contains(type.Id))
            .Select(type => type.Key)
            .Order(StringComparer.Ordinal)];
    }
}

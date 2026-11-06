namespace Starter.Modules.Attachments.Domain;

/// <summary>
/// The start-up check that keeps ONE upload allowlist: every extension an attachment type allows must be in
/// <c>Codelab:Security:Uploads:AllowedExtensions</c> and not in its <c>BlockedExtensions</c>, so a type can never
/// promise a file the attachment service refuses (or accept one the platform blocks).
/// </summary>
public static class AttachmentTypeCatalog
{
    /// <summary>
    /// Every (type, extension) pair of <paramref name="types"/> that <paramref name="allowlist"/> does not accept,
    /// ordered by type key then extension; empty when the catalog fits the allowlist. Comparisons ignore case.
    /// </summary>
    public static IReadOnlyList<AttachmentTypeViolation> FindViolations(IEnumerable<ServiceAttachment> types, UploadAllowlist allowlist)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(allowlist);
        var allowed = allowlist.Allowed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var blocked = allowlist.Blocked.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. types
            .SelectMany(type => type.AllowedExtensions.Select(extension => new AttachmentTypeViolation(type.Key, extension)))
            .Where(pair => !allowed.Contains(pair.Extension) || blocked.Contains(pair.Extension))
            .OrderBy(pair => pair.TypeKey, StringComparer.Ordinal)
            .ThenBy(pair => pair.Extension, StringComparer.Ordinal)];
    }
}

/// <summary>The platform upload allowlist (<c>Codelab:Security:Uploads</c>), as the catalog check reads it.</summary>
/// <param name="Allowed">The allowed extensions, with the dot.</param>
/// <param name="Blocked">The always-refused extensions, with the dot.</param>
public sealed record UploadAllowlist(IReadOnlyCollection<string> Allowed, IReadOnlyCollection<string> Blocked);

/// <summary>An attachment type extension the platform allowlist does not accept.</summary>
/// <param name="TypeKey">The attachment type key.</param>
/// <param name="Extension">The extension, with the dot.</param>
public sealed record AttachmentTypeViolation(string TypeKey, string Extension);

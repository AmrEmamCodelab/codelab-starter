using Codelab.Security.Uploads;

namespace Starter.Modules.Attachments.Domain;

/// <summary>
/// The one owner of how an attachment type's allowed extensions look and are stored: lower-case, with the leading dot
/// (<c>.pdf</c>), stored as one <c>;</c>-joined column value (<c>.pdf;.png</c>). Migrations write the stored form with
/// <see cref="ToStorage"/>.
/// </summary>
public static class AttachmentExtensions
{
    /// <summary>The separator of the stored form.</summary>
    public const char Separator = ';';

    private const char Dot = '.';

    /// <summary>
    /// The stored form of <paramref name="extensions"/>: each normalised (<see cref="Normalize"/>), duplicates dropped,
    /// in the given order, joined with <see cref="Separator"/>.
    /// </summary>
    /// <exception cref="ArgumentException">An extension is not well formed, or the list is empty or too long.</exception>
    public static string ToStorage(IEnumerable<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        var normalized = extensions.Select(Normalize).Distinct(StringComparer.Ordinal).ToList();
        if (normalized.Count == 0)
        {
            throw new ArgumentException("An attachment type needs at least one allowed extension.", nameof(extensions));
        }

        var stored = string.Join(Separator, normalized);
        return stored.Length <= AttachmentsLimits.AllowedExtensionsMaxLength
            ? stored
            : throw new ArgumentException("The allowed extensions are longer than the column.", nameof(extensions));
    }

    /// <summary>The extensions of a stored value (empty for an empty value).</summary>
    public static IReadOnlyList<string> FromStorage(string? stored) =>
        string.IsNullOrEmpty(stored) ? [] : stored.Split(Separator);

    /// <summary>
    /// <paramref name="extension"/> lower-cased, with a leading dot added when missing (<c>PDF</c> becomes <c>.pdf</c>).
    /// </summary>
    /// <exception cref="ArgumentException">Not a dot followed by 1 to 15 ASCII letters or digits.</exception>
    public static string Normalize(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        var lower = extension.Trim().ToLowerInvariant();
        var withDot = lower[0] == Dot ? lower : Dot + lower;
        return IsWellFormed(withDot)
            ? withDot
            : throw new ArgumentException($"'{extension}' is not a file extension.", nameof(extension));
    }

    /// <summary>
    /// The lower-case extension of <paramref name="fileName"/> as the upload checks see it (trailing dots and spaces
    /// ignored, a stream suffix yields none), or empty.
    /// </summary>
    public static string Of(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return FileNames.NormalizedExtension(fileName).ToLowerInvariant();
    }

    private static bool IsWellFormed(string extension) =>
        extension.Length > 1 && extension.Length <= AttachmentsLimits.ExtensionMaxLength && extension.Skip(1).All(char.IsAsciiLetterOrDigit);
}

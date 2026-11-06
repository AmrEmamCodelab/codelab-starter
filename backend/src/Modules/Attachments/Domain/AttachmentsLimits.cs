using Starter.Domain.Lookups;

namespace Starter.Modules.Attachments.Domain;

/// <summary>
/// The Attachments module's column sizes and shape rules (one owner for the entities, the validators and the EF
/// configuration).
/// </summary>
public static class AttachmentsLimits
{
    /// <summary>The longest attachment type key (<see cref="ServiceAttachment.Key"/>).</summary>
    public const int TypeKeyMaxLength = 64;

    /// <summary>The longest record type (<see cref="RecordAttachment.RecordType"/>).</summary>
    public const int RecordTypeMaxLength = 64;

    /// <summary>The longest Arabic or English type name.</summary>
    public const int NameMaxLength = 200;

    /// <summary>The most files one record may hold under one type (the ceiling a seeded <see cref="ServiceAttachment.MaxFiles"/> may not pass).</summary>
    public const int MaxFilesCeiling = 50;

    /// <summary>The longest stored list of allowed extensions (<see cref="AttachmentExtensions.ToStorage"/>).</summary>
    public const int AllowedExtensionsMaxLength = 256;

    /// <summary>The longest single extension, dot included.</summary>
    public const int ExtensionMaxLength = 16;

    /// <summary>The shortest attachment-service file id (22 base64url characters carry 128 random bits).</summary>
    public const int FileIdMinLength = 22;

    /// <summary>The longest attachment-service file id.</summary>
    public const int FileIdMaxLength = 64;

    /// <summary>The longest stored file name.</summary>
    public const int FileNameMaxLength = 255;

    /// <summary>The longest stored MIME type.</summary>
    public const int ContentTypeMaxLength = 127;

    /// <summary>The length of a lower-case hex SHA-256.</summary>
    public const int Sha256HexLength = 64;

    /// <summary>The longest upload receipt a link request may carry.</summary>
    public const int ReceiptMaxLength = 4096;

    /// <summary>True when <paramref name="typeKey"/> is a well-formed attachment type key (kebab-case ASCII).</summary>
    public static bool IsTypeKey(string? typeKey) => LookupLimits.IsKebab(typeKey, TypeKeyMaxLength);

    /// <summary>True when <paramref name="recordType"/> is a well-formed record type (kebab-case ASCII).</summary>
    public static bool IsRecordType(string? recordType) => LookupLimits.IsKebab(recordType, RecordTypeMaxLength);

    /// <summary>
    /// True when <paramref name="fileId"/> can be an attachment-service file id: URL-safe base64 characters only
    /// (letters, digits, <c>-</c>, <c>_</c>), between <see cref="FileIdMinLength"/> and <see cref="FileIdMaxLength"/>.
    /// </summary>
    public static bool IsFileId(string? fileId) =>
        fileId is { Length: >= FileIdMinLength and <= FileIdMaxLength } && fileId.All(IsUrlSafe);

    /// <summary>True when <paramref name="sha256Hex"/> is a lower-case hex SHA-256.</summary>
    public static bool IsSha256Hex(string? sha256Hex) =>
        sha256Hex is { Length: Sha256HexLength } && sha256Hex.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');

    /// <summary>
    /// True when <paramref name="fileName"/> can be stored and later written into a <c>Content-Disposition</c> header:
    /// not blank, at most <see cref="FileNameMaxLength"/>, a leaf name (no <c>/</c>, <c>\</c> or <c>:</c>), and free
    /// of double quotes and control characters.
    /// </summary>
    public static bool IsStorableFileName(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && fileName.Length <= FileNameMaxLength && !fileName.Any(IsUnstorable);

    private static bool IsUrlSafe(char character) => char.IsAsciiLetterOrDigit(character) || character is '-' or '_';

    private static bool IsUnstorable(char character) => character is '"' or '/' or '\\' or ':' || char.IsControl(character);
}

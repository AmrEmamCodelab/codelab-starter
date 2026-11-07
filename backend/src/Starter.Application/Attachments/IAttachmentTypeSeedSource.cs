namespace Starter.Application.Attachments;

/// <summary>
/// The attachment types one module owns, written to the Attachments catalog (<c>attachments.ServiceAttachments</c>) by
/// the Attachments module on every start: a missing type is inserted, a changed one refreshed (never its id, key,
/// record type or active flag), an equal one left alone, matched by key. The owning module registers one source, so a
/// record type's attachment types exist exactly when its module is in the host, and deleting the module deletes its
/// types' declaration with it (rows already written stay). Stable for the process lifetime.
/// </summary>
public interface IAttachmentTypeSeedSource
{
    /// <summary>The module's attachment types; ids and keys unique across every source.</summary>
    IEnumerable<AttachmentTypeDefinition> Types { get; }
}

/// <summary>
/// One attachment type as its owning module declares it. Validated by the Attachments module at start (a malformed
/// definition, a duplicate id or key, or an extension outside the upload allowlist stops the host).
/// </summary>
/// <param name="Id">The fixed id, a <c>Starter.Domain.Attachments.AttachmentTypeIds</c> constant (never reused or renumbered).</param>
/// <param name="Key">The kebab-case key, an <c>AttachmentTypeKeys</c> constant (the <c>typeKey</c> the client sends).</param>
/// <param name="RecordType">The record type whose records carry the type, an <c>AttachableRecordTypes</c> constant.</param>
/// <param name="NameAr">The Arabic display name.</param>
/// <param name="NameEn">The English display name.</param>
/// <param name="IsRequired">True when a record needs one before it can move on (the required-attachments interlock).</param>
/// <param name="MaxFiles">The most live files one record may hold under the type.</param>
/// <param name="AllowedExtensions">The accepted extensions (<c>.pdf</c> or <c>pdf</c>), inside the upload allowlist.</param>
public sealed record AttachmentTypeDefinition(
    int Id,
    string Key,
    string RecordType,
    string NameAr,
    string NameEn,
    bool IsRequired,
    int MaxFiles,
    IReadOnlyList<string> AllowedExtensions);

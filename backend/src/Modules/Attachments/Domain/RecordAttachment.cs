namespace Starter.Modules.Attachments.Domain;

/// <summary>
/// One file linked to one record under one attachment type (<c>attachments.RecordAttachments</c>). Generic: the
/// record is named by its record type and internal id, never by a foreign key into another module. The file bytes
/// live on the attachment service under <see cref="FileId"/>; this row keeps the receipt-verified metadata only.
/// Removing is soft (<see cref="RemovedAt"/>): the row stays for the history, the bytes are deleted later through the
/// outbox.
/// </summary>
public sealed class RecordAttachment
{
    private RecordAttachment()
    {
    }

    /// <summary>The generated id (on the wire as a PublicId).</summary>
    public long Id { get; private set; }

    /// <summary>The record type (an <c>AttachableRecordTypes</c> constant).</summary>
    public string RecordType { get; private set; } = string.Empty;

    /// <summary>The record's internal id within its record type.</summary>
    public long RecordId { get; private set; }

    /// <summary>The attachment type (<see cref="ServiceAttachment.Id"/>, an <c>AttachmentTypeIds</c> constant).</summary>
    public int ServiceAttachmentId { get; private set; }

    /// <summary>The attachment-service file id (opaque, URL-safe, unique across every link).</summary>
    public string FileId { get; private set; } = string.Empty;

    /// <summary>The sanitised client file name, as the receipt attests it.</summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>The size in bytes, as the receipt attests it.</summary>
    public long SizeBytes { get; private set; }

    /// <summary>The lower-case hex SHA-256, as the receipt attests it (never a client-sent value).</summary>
    public string Sha256Hex { get; private set; } = string.Empty;

    /// <summary>The MIME type the upload allowlist assigns to the file's extension.</summary>
    public string ContentType { get; private set; } = string.Empty;

    /// <summary>The token subject of the user who linked the file.</summary>
    public Guid UploadedBySubject { get; private set; }

    /// <summary>When the file was linked.</summary>
    public DateTimeOffset UploadedAt { get; private set; }

    /// <summary>When the attachment was removed, or null while it is live.</summary>
    public DateTimeOffset? RemovedAt { get; private set; }

    /// <summary>The token subject of the user who removed it, or null while it is live.</summary>
    public Guid? RemovedBySubject { get; private set; }

    /// <summary>True while the attachment is not removed.</summary>
    public bool IsLive => RemovedAt is null;

    /// <summary>
    /// A new link of <paramref name="file"/> to <paramref name="owner"/>. The caller verified the receipt and the type
    /// rules first; a throw here is a bug, not a user error.
    /// </summary>
    /// <exception cref="ArgumentException">A value is not well formed (see <see cref="AttachmentsLimits"/>).</exception>
    public static RecordAttachment Link(AttachmentOwner owner, StoredFile file, AttachmentStamp uploaded)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(uploaded);
        owner.EnsureWellFormed();
        file.EnsureWellFormed();
        return new RecordAttachment
        {
            RecordType = owner.RecordType,
            RecordId = owner.RecordId,
            ServiceAttachmentId = owner.ServiceAttachmentId,
            FileId = file.FileId,
            FileName = file.FileName,
            SizeBytes = file.SizeBytes,
            Sha256Hex = file.Sha256Hex,
            ContentType = file.ContentType,
            UploadedBySubject = uploaded.Subject,
            UploadedAt = uploaded.At,
        };
    }

    /// <summary>True when this attachment belongs to <paramref name="owner"/> (same record and type).</summary>
    public bool BelongsTo(AttachmentOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return string.Equals(RecordType, owner.RecordType, StringComparison.Ordinal)
            && RecordId == owner.RecordId
            && ServiceAttachmentId == owner.ServiceAttachmentId;
    }

    /// <summary>Removes the attachment (soft); false when it was already removed (the first removal is kept).</summary>
    public bool Remove(AttachmentStamp removed)
    {
        ArgumentNullException.ThrowIfNull(removed);
        if (!IsLive)
        {
            return false;
        }

        RemovedAt = removed.At;
        RemovedBySubject = removed.Subject;
        return true;
    }
}

/// <summary>Where an attachment belongs: one record under one attachment type.</summary>
/// <param name="RecordType">The record type (an <c>AttachableRecordTypes</c> constant).</param>
/// <param name="RecordId">The record's internal id (at least 1).</param>
/// <param name="ServiceAttachmentId">The attachment type id.</param>
public sealed record AttachmentOwner(string RecordType, long RecordId, int ServiceAttachmentId)
{
    internal void EnsureWellFormed()
    {
        if (!AttachmentsLimits.IsRecordType(RecordType))
        {
            throw new ArgumentException("The record type must be kebab-case.", nameof(RecordType));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(RecordId, 1, nameof(RecordId));
        ArgumentOutOfRangeException.ThrowIfLessThan(ServiceAttachmentId, 1, nameof(ServiceAttachmentId));
    }
}

/// <summary>A file the attachment service stored, as its verified receipt describes it.</summary>
/// <param name="FileId">The service's opaque file id.</param>
/// <param name="FileName">The sanitised file name.</param>
/// <param name="SizeBytes">The size in bytes (at least 1).</param>
/// <param name="Sha256Hex">The lower-case hex SHA-256.</param>
/// <param name="ContentType">The MIME type for the file's extension.</param>
public sealed record StoredFile(string FileId, string FileName, long SizeBytes, string Sha256Hex, string ContentType)
{
    /// <summary>
    /// True when every value can be stored: a well-formed file id, a storable leaf file name, a lower-case hex SHA-256,
    /// a MIME type within <see cref="AttachmentsLimits.ContentTypeMaxLength"/> and a size of at least one byte.
    /// </summary>
    public bool IsWellFormed() =>
        AttachmentsLimits.IsFileId(FileId)
        && AttachmentsLimits.IsStorableFileName(FileName)
        && AttachmentsLimits.IsSha256Hex(Sha256Hex)
        && !string.IsNullOrWhiteSpace(ContentType) && ContentType.Length <= AttachmentsLimits.ContentTypeMaxLength
        && SizeBytes >= 1;

    internal void EnsureWellFormed()
    {
        if (!IsWellFormed())
        {
            throw new ArgumentException("The stored file has a value that is not well formed.", nameof(StoredFile));
        }
    }
}

/// <summary>Who did something to an attachment, and when.</summary>
/// <param name="Subject">The user's token subject.</param>
/// <param name="At">The time, from <see cref="TimeProvider"/>.</param>
public sealed record AttachmentStamp(Guid Subject, DateTimeOffset At);

using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Application;

/// <summary>One attachment type as the client sees it.</summary>
/// <param name="Key">The type key.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="NameEn">The English name.</param>
/// <param name="IsRequired">True when a record needs one before it can move on.</param>
/// <param name="MaxFiles">The most files per record.</param>
/// <param name="AllowedExtensions">The accepted extensions, lower-case with the dot.</param>
public sealed record AttachmentTypeView(string Key, string NameAr, string NameEn, bool IsRequired, int MaxFiles, IReadOnlyList<string> AllowedExtensions)
{
    /// <summary>The view of <paramref name="type"/>.</summary>
    public static AttachmentTypeView From(ServiceAttachment type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new AttachmentTypeView(type.Key, type.NameAr, type.NameEn, type.IsRequired, type.MaxFiles, type.AllowedExtensions);
    }
}

/// <summary>One live attachment as the client sees it (the API turns <paramref name="Id"/> into a PublicId).</summary>
/// <param name="Id">The attachment id.</param>
/// <param name="TypeKey">The attachment type key.</param>
/// <param name="FileName">The file name.</param>
/// <param name="SizeBytes">The size in bytes.</param>
/// <param name="ContentType">The MIME type.</param>
/// <param name="UploadedAt">When it was linked.</param>
public sealed record RecordAttachmentView(long Id, string TypeKey, string FileName, long SizeBytes, string ContentType, DateTimeOffset UploadedAt)
{
    /// <summary>The view of <paramref name="attachment"/> under the type key <paramref name="typeKey"/>.</summary>
    public static RecordAttachmentView From(RecordAttachment attachment, string typeKey)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return new RecordAttachmentView(attachment.Id, typeKey, attachment.FileName, attachment.SizeBytes, attachment.ContentType, attachment.UploadedAt);
    }
}

/// <summary>A record's live attachments and the required types it still misses.</summary>
/// <param name="Items">The live attachments, ordered by upload time.</param>
/// <param name="MissingRequiredTypeKeys">The keys of the required types with no attachment (the interlock's answer).</param>
public sealed record RecordAttachmentsView(IReadOnlyList<RecordAttachmentView> Items, IReadOnlyList<string> MissingRequiredTypeKeys);

/// <summary>What the browser needs to upload straight to the attachment service.</summary>
/// <param name="Ticket">The sealed, single-use ticket.</param>
/// <param name="TicketHeader">The header to send it in.</param>
/// <param name="UploadUrl">The absolute upload URL.</param>
/// <param name="ExpiresAt">When the ticket stops opening.</param>
/// <param name="AllowedExtensions">The extensions the type accepts.</param>
/// <param name="MaxFileCount">How many more files of the type the record may take.</param>
public sealed record UploadTicketView(
    string Ticket, string TicketHeader, string UploadUrl, DateTimeOffset ExpiresAt, IReadOnlyList<string> AllowedExtensions, int MaxFileCount);

/// <summary>An attachment's bytes with the metadata this database recorded (never the service's echo).</summary>
/// <param name="Content">The byte stream; the caller disposes it.</param>
/// <param name="FileName">The file name for <c>Content-Disposition</c>.</param>
/// <param name="ContentType">The MIME type.</param>
/// <param name="SizeBytes">The size in bytes.</param>
public sealed record AttachmentDownload(Stream Content, string FileName, string ContentType, long SizeBytes);

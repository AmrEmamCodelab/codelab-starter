using Codelab.Security.PublicIds;
using Starter.Modules.Attachments.Application;

namespace Starter.Modules.Attachments.Api;

/// <summary>One active attachment type of <c>GET /api/attachments/types?recordType={key}</c>.</summary>
/// <param name="Key">The type key (kebab-case; the <c>typeKey</c> of tickets and links).</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="NameEn">The English name.</param>
/// <param name="IsRequired">True when a record needs one before it can move on.</param>
/// <param name="MaxFiles">The most files of this type per record.</param>
/// <param name="AllowedExtensions">The accepted extensions, lower-case with the dot.</param>
public sealed record AttachmentTypeResponse(
    string Key, string NameAr, string NameEn, bool IsRequired, int MaxFiles, IReadOnlyList<string> AllowedExtensions)
{
    /// <summary>The wire list of <paramref name="types"/>, in their order.</summary>
    public static IReadOnlyList<AttachmentTypeResponse> ListFrom(IReadOnlyList<AttachmentTypeView> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        return [.. types.Select(type => new AttachmentTypeResponse(
            type.Key, type.NameAr, type.NameEn, type.IsRequired, type.MaxFiles, type.AllowedExtensions))];
    }
}

/// <summary>One live attachment of a record (list item and the 201 body of a link).</summary>
/// <param name="Id">The attachment id (opaque token, family <c>attachments</c>).</param>
/// <param name="TypeKey">The attachment type key.</param>
/// <param name="FileName">The file name as uploaded (sanitized by the attachment service).</param>
/// <param name="SizeBytes">The size in bytes.</param>
/// <param name="ContentType">The MIME type (from the extension, never from the client).</param>
/// <param name="UploadedAt">When it was linked.</param>
public sealed record RecordAttachmentResponse(
    PublicId<AttachmentIds> Id, string TypeKey, string FileName, long SizeBytes, string ContentType, DateTimeOffset UploadedAt)
{
    /// <summary>The wire form of <paramref name="view"/>.</summary>
    public static RecordAttachmentResponse From(RecordAttachmentView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new RecordAttachmentResponse(
            PublicId<AttachmentIds>.From(view.Id), view.TypeKey, view.FileName, view.SizeBytes, view.ContentType, view.UploadedAt);
    }
}

/// <summary>The body of <c>GET /api/records/{recordType}/{recordId}/attachments</c>.</summary>
/// <param name="Items">The live attachments, ordered by upload time.</param>
/// <param name="MissingRequiredTypeKeys">The required types with no attachment yet (the interlock's answer).</param>
public sealed record RecordAttachmentsResponse(IReadOnlyList<RecordAttachmentResponse> Items, IReadOnlyList<string> MissingRequiredTypeKeys)
{
    /// <summary>The wire form of <paramref name="view"/>.</summary>
    public static RecordAttachmentsResponse From(RecordAttachmentsView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new RecordAttachmentsResponse([.. view.Items.Select(RecordAttachmentResponse.From)], view.MissingRequiredTypeKeys);
    }
}

/// <summary>The body of <c>POST /api/records/{recordType}/{recordId}/attachments/upload-ticket</c>.</summary>
/// <param name="Ticket">The sealed, single-use ticket.</param>
/// <param name="TicketHeader">The header the browser sends it in (and the only header the attachment service reads).</param>
/// <param name="UploadUrl">The absolute upload URL on the attachment service.</param>
/// <param name="ExpiresAt">When the ticket stops opening.</param>
/// <param name="AllowedExtensions">The extensions the type accepts.</param>
/// <param name="MaxFileCount">How many more files of the type the record may take.</param>
public sealed record UploadTicketResponse(
    string Ticket, string TicketHeader, string UploadUrl, DateTimeOffset ExpiresAt, IReadOnlyList<string> AllowedExtensions, int MaxFileCount)
{
    /// <summary>The wire form of <paramref name="view"/>.</summary>
    public static UploadTicketResponse From(UploadTicketView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new UploadTicketResponse(view.Ticket, view.TicketHeader, view.UploadUrl, view.ExpiresAt, view.AllowedExtensions, view.MaxFileCount);
    }
}

/// <summary>The body of <c>GET /api/attachments/demo-record</c> (Development only).</summary>
/// <param name="RecordType">The demo record type (<c>attachments-demo</c>).</param>
/// <param name="RecordId">The demo record id (opaque token, family <c>attachable-records</c>).</param>
public sealed record DemoRecordResponse(string RecordType, PublicId<AttachableRecordIds> RecordId)
{
    /// <summary>The one demo record (<see cref="Application.DemoRecord"/>).</summary>
    public static DemoRecordResponse Demo { get; } =
        new(Application.DemoRecord.RecordType, PublicId<AttachableRecordIds>.From(Application.DemoRecord.RecordId));
}

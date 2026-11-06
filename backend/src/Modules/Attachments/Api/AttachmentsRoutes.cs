using Codelab.Security.PublicIds;
using Starter.Infrastructure.Hosting;

namespace Starter.Modules.Attachments.Api;

/// <summary>The Attachments routes, built from the kernel prefix (no route literal elsewhere).</summary>
public static class AttachmentsRoutes
{
    /// <summary>The root of the attachment endpoints: <c>api/attachments</c>.</summary>
    public const string Root = ApiRoutes.Prefix + ApiRoutes.Separator + "attachments";

    /// <summary>The name of the route parameter that carries a record type key (checked by the use case; unknown answers 404).</summary>
    public const string RecordTypeParameter = "recordType";

    /// <summary>The name of the route parameter that carries a record's PublicId (family <c>attachable-records</c>).</summary>
    public const string RecordIdParameter = "recordId";

    /// <summary>The name of the route parameter that carries an attachment's PublicId (family <c>attachments</c>).</summary>
    public const string IdParameter = "id";

    /// <summary>
    /// The attachments of one record: <c>api/records/{recordType}/{recordId}/attachments</c>; the record id is a PublicId,
    /// never a raw number.
    /// </summary>
    public const string RecordAttachments =
        ApiRoutes.Prefix + ApiRoutes.Separator + "records" + ApiRoutes.Separator
        + "{" + RecordTypeParameter + "}" + ApiRoutes.Separator
        + "{" + RecordIdParameter + ":" + PublicIdRouteConstraint.ConstraintName + "}" + ApiRoutes.Separator
        + "attachments";

    /// <summary><c>GET types?recordType={key}</c> under <see cref="Root"/>.</summary>
    public const string Types = "types";

    /// <summary>The query parameter of <see cref="Types"/> that names the record type.</summary>
    public const string RecordTypeQuery = RecordTypeParameter;

    /// <summary><c>POST upload-ticket</c> under <see cref="RecordAttachments"/>.</summary>
    public const string UploadTicket = "upload-ticket";

    /// <summary>The route template of an attachment id: a PublicId, never a raw number.</summary>
    public const string IdTemplate = "{" + IdParameter + ":" + PublicIdRouteConstraint.ConstraintName + "}";

    /// <summary><c>GET {id}/content</c> under <see cref="Root"/>: the file bytes.</summary>
    public const string Content = IdTemplate + ApiRoutes.Separator + "content";

    /// <summary><c>GET demo-record</c> under <see cref="Root"/> (answers only while the Development demo record type is registered).</summary>
    public const string DemoRecord = "demo-record";

    /// <summary>The JSON response content type.</summary>
    public const string JsonContentType = "application/json";

    /// <summary>The content type the download endpoint declares in OpenAPI (the real one comes from the attachment row).</summary>
    public const string FileContentType = "application/octet-stream";
}

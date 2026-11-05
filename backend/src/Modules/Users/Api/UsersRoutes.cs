using Codelab.Security.PublicIds;
using Starter.Infrastructure.Hosting;
using Starter.Modules.Users.Application.Import;

namespace Starter.Modules.Users.Api;

/// <summary>The Users routes, built from the kernel prefix (no route literal elsewhere).</summary>
public static class UsersRoutes
{
    /// <summary>The admin area: <c>api/admin</c>.</summary>
    public const string Admin = ApiRoutes.Prefix + ApiRoutes.Separator + "admin";

    /// <summary>The user accounts: <c>api/admin/users</c>.</summary>
    public const string Users = Admin + ApiRoutes.Separator + "users";

    /// <summary>The organizations: <c>api/admin/organizations</c>.</summary>
    public const string Organizations = Admin + ApiRoutes.Separator + "organizations";

    /// <summary>The Excel user import: <c>api/admin/user-import</c>.</summary>
    public const string Import = Admin + ApiRoutes.Separator + "user-import";

    /// <summary>The name of the route parameter that carries a record's PublicId.</summary>
    public const string IdParameter = "id";

    /// <summary>The route template of a record id: a PublicId, never a raw number.</summary>
    public const string IdTemplate = "{" + IdParameter + ":" + PublicIdRouteConstraint.ConstraintName + "}";

    /// <summary>The name of the route parameter that carries an import batch's PublicId.</summary>
    public const string BatchIdParameter = "batchId";

    /// <summary>The route template of an import batch id.</summary>
    public const string BatchIdTemplate = "{" + BatchIdParameter + ":" + PublicIdRouteConstraint.ConstraintName + "}";

    /// <summary><c>POST {id}/deactivate</c>.</summary>
    public const string Deactivate = IdTemplate + ApiRoutes.Separator + "deactivate";

    /// <summary><c>POST {id}/reactivate</c>.</summary>
    public const string Reactivate = IdTemplate + ApiRoutes.Separator + "reactivate";

    /// <summary><c>POST {id}/unlock</c>.</summary>
    public const string Unlock = IdTemplate + ApiRoutes.Separator + "unlock";

    /// <summary><c>POST {id}/activation</c>.</summary>
    public const string Activation = IdTemplate + ApiRoutes.Separator + "activation";

    /// <summary><c>POST preview</c> (multipart).</summary>
    public const string Preview = "preview";

    /// <summary><c>POST {batchId}/commit</c>.</summary>
    public const string Commit = BatchIdTemplate + ApiRoutes.Separator + "commit";

    /// <summary>The multipart form field that carries the import file.</summary>
    public const string FileField = "file";

    /// <summary>The multipart content type of the preview.</summary>
    public const string MultipartContentType = "multipart/form-data";

    /// <summary>The response content type.</summary>
    public const string JsonContentType = "application/json";

    /// <summary>The bytes a multipart request may carry beyond the file (boundaries and part headers).</summary>
    public const long MultipartOverheadBytes = 64L * 1024;

    /// <summary>The largest preview request: the file cap plus the multipart overhead.</summary>
    public const long MaxPreviewRequestBytes = ImportLimits.MaxUploadBytes + MultipartOverheadBytes;
}

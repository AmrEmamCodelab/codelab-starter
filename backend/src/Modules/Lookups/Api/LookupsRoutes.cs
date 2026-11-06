using Codelab.Security.PublicIds;
using Starter.Infrastructure.Hosting;

namespace Starter.Modules.Lookups.Api;

/// <summary>The Lookups routes, built from the kernel prefix (no route literal elsewhere).</summary>
public static class LookupsRoutes
{
    /// <summary>The read API for signed-in users: <c>api/lookups</c>.</summary>
    public const string Lookups = ApiRoutes.Prefix + ApiRoutes.Separator + "lookups";

    /// <summary>The admin area: <c>api/admin/lookups</c>.</summary>
    public const string AdminLookups = ApiRoutes.Prefix + ApiRoutes.Separator + "admin" + ApiRoutes.Separator + "lookups";

    /// <summary>The name of the route parameter that carries a lookup type key.</summary>
    public const string TypeParameter = "type";

    /// <summary>The route template of a lookup type key (kebab-case; checked by the use case, unknown answers 404).</summary>
    public const string TypeTemplate = "{" + TypeParameter + "}";

    /// <summary>The name of the route parameter that carries a lookup value's PublicId.</summary>
    public const string IdParameter = "id";

    /// <summary><c>PUT {type}/items/{id}/active</c>: the value id is a PublicId, never a raw number.</summary>
    public const string ItemActive =
        TypeTemplate + ApiRoutes.Separator + "items" + ApiRoutes.Separator
        + "{" + IdParameter + ":" + PublicIdRouteConstraint.ConstraintName + "}" + ApiRoutes.Separator + "active";

    /// <summary>The response content type.</summary>
    public const string JsonContentType = "application/json";
}

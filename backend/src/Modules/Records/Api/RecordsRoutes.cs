using Codelab.Security.PublicIds;
using Starter.Infrastructure.Hosting;

namespace Starter.Modules.Records.Api;

/// <summary>The Records routes, built from the kernel prefix (no route literal elsewhere).</summary>
public static class RecordsRoutes
{
    /// <summary>The root of every Records endpoint: <c>api/records</c>.</summary>
    public const string Root = ApiRoutes.Prefix + ApiRoutes.Separator + "records";

    /// <summary>The name of the route parameter that carries the record type key.</summary>
    public const string RecordTypeParameter = "recordType";

    /// <summary>The name of the route parameter that carries a record's PublicId.</summary>
    public const string IdParameter = "id";

    /// <summary>The routes of one record type: <c>api/records/{recordType}</c>.</summary>
    public const string RecordType = Root + ApiRoutes.Separator + "{" + RecordTypeParameter + "}";

    /// <summary>The route template of a record id: a PublicId, never a raw number.</summary>
    public const string IdTemplate = "{" + IdParameter + ":" + PublicIdRouteConstraint.ConstraintName + "}";

    /// <summary>The caller's own records.</summary>
    public const string Mine = "mine";

    /// <summary>The admin oversight list.</summary>
    public const string Oversight = "oversight";

    /// <summary>A record's timeline.</summary>
    public const string Timeline = IdTemplate + ApiRoutes.Separator + "timeline";

    /// <summary>A record's transitions.</summary>
    public const string Transitions = IdTemplate + ApiRoutes.Separator + "transitions";

    /// <summary>The status filter query parameter.</summary>
    public const string StatusQuery = "status";

    /// <summary>The number filter query parameter.</summary>
    public const string NumberQuery = "number";

    /// <summary>The page query parameter (1-based).</summary>
    public const string PageQuery = "page";

    /// <summary>The page size query parameter.</summary>
    public const string PageSizeQuery = "pageSize";

    /// <summary>The JSON content type.</summary>
    public const string JsonContentType = "application/json";
}

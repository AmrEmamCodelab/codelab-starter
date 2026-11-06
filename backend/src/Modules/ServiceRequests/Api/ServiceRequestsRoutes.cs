using Codelab.Security.PublicIds;
using Starter.Infrastructure.Hosting;

namespace Starter.Modules.ServiceRequests.Api;

/// <summary>The ServiceRequests routes, built from the kernel prefix (no route literal elsewhere).</summary>
public static class ServiceRequestsRoutes
{
    /// <summary>The root of every ServiceRequests endpoint: <c>/api/service-requests</c>.</summary>
    public const string Root = ApiRoutes.Prefix + ApiRoutes.Separator + "service-requests";

    /// <summary>The name of the route parameter that carries a record's PublicId.</summary>
    public const string IdParameter = "id";

    /// <summary>The route template of a record id: a PublicId, never a raw number.</summary>
    public const string IdTemplate = "{" + IdParameter + ":" + PublicIdRouteConstraint.ConstraintName + "}";

    /// <summary>The draft of one record: <c>{id}/draft</c>.</summary>
    public const string Draft = IdTemplate + ApiRoutes.Separator + "draft";

    /// <summary>The submit of one record: <c>{id}/submit</c>.</summary>
    public const string Submit = IdTemplate + ApiRoutes.Separator + "submit";

    /// <summary>The JSON content type every endpoint produces.</summary>
    public const string JsonContentType = "application/json";
}

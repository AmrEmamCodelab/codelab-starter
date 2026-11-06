using Codelab.Security.PublicIds;
using Starter.Infrastructure.Hosting;

namespace Starter.Modules.Notifications.Api;

/// <summary>The Notifications routes, built from the kernel prefix (no route literal elsewhere).</summary>
public static class NotificationsRoutes
{
    /// <summary>The caller's own feed: <c>api/notifications</c>.</summary>
    public const string Root = ApiRoutes.Prefix + ApiRoutes.Separator + "notifications";

    /// <summary>The admin notification tools: <c>api/admin/notifications</c>.</summary>
    public const string Admin = ApiRoutes.Prefix + ApiRoutes.Separator + "admin" + ApiRoutes.Separator + "notifications";

    /// <summary>The name of the route parameter that carries a notification's PublicId.</summary>
    public const string IdParameter = "id";

    /// <summary>The route template of a notification id: a PublicId, never a raw number.</summary>
    public const string IdTemplate = "{" + IdParameter + ":" + PublicIdRouteConstraint.ConstraintName + "}";

    /// <summary><c>GET unread-count</c>.</summary>
    public const string UnreadCount = "unread-count";

    /// <summary><c>POST {id}/read</c>.</summary>
    public const string Read = IdTemplate + ApiRoutes.Separator + "read";

    /// <summary><c>POST test</c> under <see cref="Admin"/>.</summary>
    public const string Test = "test";

    /// <summary>The query parameter of the feed cursor (the oldest item's PublicId the caller has).</summary>
    public const string BeforeParameter = "before";

    /// <summary>The query parameter of the feed page size.</summary>
    public const string TakeParameter = "take";

    /// <summary>The JSON content type of every answer.</summary>
    public const string JsonContentType = "application/json";
}

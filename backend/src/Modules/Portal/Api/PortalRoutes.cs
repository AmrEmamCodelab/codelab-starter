using Starter.Infrastructure.Hosting;

namespace Starter.Modules.Portal.Api;

/// <summary>The Portal routes, built from the kernel prefix (no route literal elsewhere).</summary>
public static class PortalRoutes
{
    /// <summary>The public portal: <c>api/portal</c>.</summary>
    public const string Root = ApiRoutes.Prefix + ApiRoutes.Separator + "portal";

    /// <summary>The admin portal tools: <c>api/admin/portal</c>.</summary>
    public const string Admin = ApiRoutes.Prefix + ApiRoutes.Separator + "admin" + ApiRoutes.Separator + "portal";

    /// <summary><c>POST contact-messages</c> under <see cref="Root"/>.</summary>
    public const string ContactMessages = "contact-messages";

    /// <summary>The name of the route parameter that carries a page key (<c>faq</c>, <c>service-guide</c>, <c>privacy</c>, <c>terms</c>).</summary>
    public const string PageParameter = "page";

    /// <summary><c>content/{page}</c> under <see cref="Root"/> and <see cref="Admin"/>.</summary>
    public const string Content = "content" + ApiRoutes.Separator + "{" + PageParameter + "}";

    /// <summary>The JSON content type of every answer.</summary>
    public const string JsonContentType = "application/json";
}

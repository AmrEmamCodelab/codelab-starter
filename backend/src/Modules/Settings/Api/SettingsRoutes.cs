using Starter.Infrastructure.Hosting;

namespace Starter.Modules.Settings.Api;

/// <summary>The Settings routes, built from the kernel prefix (no route literal elsewhere).</summary>
public static class SettingsRoutes
{
    /// <summary>The admin settings: <c>api/admin/settings</c>.</summary>
    public const string Admin = ApiRoutes.Prefix + ApiRoutes.Separator + "admin" + ApiRoutes.Separator + "settings";

    /// <summary>The name of the route parameter that carries a setting key (a stable key, not a record id).</summary>
    public const string KeyParameter = "key";

    /// <summary>The route template of a setting key: <c>{key}</c>.</summary>
    public const string KeyTemplate = "{" + KeyParameter + "}";

    /// <summary>The response content type.</summary>
    public const string JsonContentType = "application/json";
}

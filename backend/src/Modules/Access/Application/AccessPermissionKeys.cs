namespace Starter.Modules.Access.Application;

/// <summary>
/// The permission keys the Access module declares (format: <c>Starter.Application.Authorization.PermissionKeyFormat</c>).
/// Guard endpoints with these constants, never with literals: <c>[HasPermission(AccessPermissionKeys.DashboardView)]</c>.
/// </summary>
public static class AccessPermissionKeys
{
    /// <summary>Open the dashboard (the signed-in home page).</summary>
    public const string DashboardView = "dashboard.view";

    /// <summary>Open the access administration area.</summary>
    public const string AccessAdminView = "access.admin.view";
}

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

    /// <summary>See the roles, their permissions and the permission catalog.</summary>
    public const string RolesView = "access.roles.view";

    /// <summary>Create roles and change their names, permissions and active state.</summary>
    public const string RolesManage = "access.roles.manage";

    /// <summary>See a user's roles, direct grants, virtual roles and effective permissions.</summary>
    public const string GrantsView = "access.grants.view";

    /// <summary>Assign roles to users and grant permissions directly, with a scope.</summary>
    public const string GrantsManage = "access.grants.manage";
}

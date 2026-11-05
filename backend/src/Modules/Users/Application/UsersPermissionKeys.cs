namespace Starter.Modules.Users.Application;

/// <summary>
/// The permission keys the Users module declares (format: <c>Starter.Application.Authorization.PermissionKeyFormat</c>).
/// Guard endpoints with these constants, never with literals. Add one with
/// <c>node tools/scaffold/new-permission.mjs --module Users --action &lt;kebab&gt;</c>.
/// </summary>
public static class UsersPermissionKeys
{
    /// <summary>List and open user accounts.</summary>
    public const string View = "users.view";

    /// <summary>Create, edit, deactivate, reactivate and unlock accounts, and issue activation links.</summary>
    public const string Manage = "users.manage";

    /// <summary>Preview and commit an Excel user import.</summary>
    public const string Import = "users.import";

    /// <summary>List and open organizations.</summary>
    public const string OrganizationsView = "organizations.view";

    /// <summary>Create, edit, deactivate and reactivate organizations.</summary>
    public const string OrganizationsManage = "organizations.manage";

    // scaffold:permission-keys
}

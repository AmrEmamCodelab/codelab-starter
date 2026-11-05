using Starter.Application.Authorization;

namespace Starter.Modules.Users.Application;

/// <summary>The Users module's share of the permission catalog.</summary>
public sealed class UsersPermissionCatalogSource : IPermissionCatalogSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(Key: UsersPermissionKeys.View, Description: "List and open user accounts."),
        new(Key: UsersPermissionKeys.Manage, Description: "Create, edit, deactivate, reactivate and unlock user accounts, and issue activation links."),
        new(Key: UsersPermissionKeys.Import, Description: "Preview and commit an Excel user import."),
        new(Key: UsersPermissionKeys.OrganizationsView, Description: "List and open organizations."),
        new(Key: UsersPermissionKeys.OrganizationsManage, Description: "Create, edit, deactivate and reactivate organizations."),
        // scaffold:permission-catalog
    ];
}

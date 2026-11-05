using Starter.Application.Authorization;

namespace Starter.Modules.Access.Application;

/// <summary>The Access module's share of the permission catalog.</summary>
public sealed class AccessPermissionCatalogSource : IPermissionCatalogSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(Key: AccessPermissionKeys.DashboardView, Description: "Open the dashboard."),
        new(Key: AccessPermissionKeys.AccessAdminView, Description: "Open the access administration area."),
        new(Key: AccessPermissionKeys.RolesView, Description: "See the roles, their permissions and the permission catalog."),
        new(Key: AccessPermissionKeys.RolesManage, Description: "Create roles and change their names, permissions and active state."),
        new(Key: AccessPermissionKeys.GrantsView, Description: "See a user's roles, direct grants, virtual roles and effective permissions."),
        new(Key: AccessPermissionKeys.GrantsManage, Description: "Assign roles to users and grant permissions to them directly, with a scope."),
    ];
}

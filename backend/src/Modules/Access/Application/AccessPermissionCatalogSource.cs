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
    ];
}

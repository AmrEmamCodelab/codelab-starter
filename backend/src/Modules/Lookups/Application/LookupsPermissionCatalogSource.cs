using Starter.Application.Authorization;

namespace Starter.Modules.Lookups.Application;

/// <summary>The Lookups module's share of the permission catalog.</summary>
public sealed class LookupsPermissionCatalogSource : IPermissionCatalogSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(Key: LookupsPermissionKeys.View, Description: "List the lookup types and every value of a type, active or not."),
        new(Key: LookupsPermissionKeys.Manage, Description: "Activate and deactivate lookup values."),
        // scaffold:permission-catalog
    ];
}

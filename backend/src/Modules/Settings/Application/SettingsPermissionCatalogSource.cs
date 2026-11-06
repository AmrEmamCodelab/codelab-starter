using Starter.Application.Authorization;

namespace Starter.Modules.Settings.Application;

/// <summary>The Settings module's share of the permission catalog.</summary>
public sealed class SettingsPermissionCatalogSource : IPermissionCatalogSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(Key: SettingsPermissionKeys.View, Description: "List the runtime settings with their values and defaults."),
        new(Key: SettingsPermissionKeys.Manage, Description: "Change the value of a runtime setting."),
        // scaffold:permission-catalog
    ];
}

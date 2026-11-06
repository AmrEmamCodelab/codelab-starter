namespace Starter.Modules.Settings.Application;

/// <summary>
/// The permission keys the Settings module declares (format: <c>Starter.Application.Authorization.PermissionKeyFormat</c>).
/// Guard endpoints with these constants, never with literals. Add one with
/// <c>node tools/scaffold/new-permission.mjs --module Settings --action &lt;kebab&gt;</c>.
/// </summary>
public static class SettingsPermissionKeys
{
    /// <summary>List the runtime settings with their values and defaults.</summary>
    public const string View = "settings.view";

    /// <summary>Change the value of a runtime setting.</summary>
    public const string Manage = "settings.manage";

    // scaffold:permission-keys
}

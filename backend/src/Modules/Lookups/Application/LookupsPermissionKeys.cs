namespace Starter.Modules.Lookups.Application;

/// <summary>
/// The permission keys the Lookups module declares (format: <c>Starter.Application.Authorization.PermissionKeyFormat</c>).
/// Guard endpoints with these constants, never with literals. Add one with
/// <c>node tools/scaffold/new-permission.mjs --module Lookups --action &lt;kebab&gt;</c>.
/// </summary>
public static class LookupsPermissionKeys
{
    /// <summary>List the lookup types and every value of a type, active or not (the admin lookups screen).</summary>
    public const string View = "lookups.view";

    /// <summary>Activate and deactivate lookup values.</summary>
    public const string Manage = "lookups.manage";

    // scaffold:permission-keys
}

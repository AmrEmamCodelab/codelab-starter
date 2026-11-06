namespace Starter.Modules.Audit.Application;

/// <summary>
/// The permission keys the Audit module declares (format: <c>Starter.Application.Authorization.PermissionKeyFormat</c>).
/// Guard endpoints with these constants, never with literals. Add one with
/// <c>node tools/scaffold/new-permission.mjs --module Audit --action &lt;kebab&gt;</c>.
/// </summary>
public static class AuditPermissionKeys
{
    /// <summary>Read the data-level audit trail (values already masked).</summary>
    public const string View = "audit.view";

    // scaffold:permission-keys
}

namespace Starter.Modules.Records.Application;

/// <summary>
/// The permission keys the Records module declares (format: <c>Starter.Application.Authorization.PermissionKeyFormat</c>).
/// Guard endpoints with these constants, never with literals. The per-record-type keys (view own, queue) come from each
/// record type's <c>RecordTypeRegistration</c> and its workflow catalog, not from here. Add one with
/// <c>node tools/scaffold/new-permission.mjs --module Records --action &lt;kebab&gt;</c>.
/// </summary>
public static class RecordsPermissionKeys
{
    /// <summary>Read every record of every record type, read-only (the admin "all requests" oversight).</summary>
    public const string OversightView = "records.oversight.view";

    // scaffold:permission-keys
}

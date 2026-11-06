namespace Starter.Modules.Attachments.Application;

/// <summary>
/// The permission keys the Attachments module declares (format: <c>Starter.Application.Authorization.PermissionKeyFormat</c>).
/// Guard endpoints with these constants, never with literals. Add one with
/// <c>node tools/scaffold/new-permission.mjs --module Attachments --action &lt;kebab&gt;</c>. A key opens the endpoint;
/// the record's own <c>IAttachableRecordAccess</c> still decides per record.
/// </summary>
public static class AttachmentsPermissionKeys
{
    /// <summary>List attachment types and a record's attachments, and download them.</summary>
    public const string View = "attachments.view";

    /// <summary>Get an upload ticket and link uploaded files to a record.</summary>
    public const string Upload = "attachments.upload";

    /// <summary>Remove a record's attachments.</summary>
    public const string Remove = "attachments.remove";

    // scaffold:permission-keys
}

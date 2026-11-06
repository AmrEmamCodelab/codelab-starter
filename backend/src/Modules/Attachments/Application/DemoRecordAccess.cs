using Starter.Application.Attachments;
using Starter.Application.Authorization;
using Starter.Domain.Attachments;

namespace Starter.Modules.Attachments.Application;

/// <summary>The Development-only demo record: one fixed record of record type <see cref="AttachableRecordTypes.Demo"/>.</summary>
public static class DemoRecord
{
    /// <summary>The record type.</summary>
    public const string RecordType = AttachableRecordTypes.Demo;

    /// <summary>The one demo record id (<c>GET /api/attachments/demo-record</c> hands it out as a PublicId).</summary>
    public const long RecordId = 1;
}

/// <summary>
/// The access of the Development demo record type: permission-based only (there is no record row). Viewing needs
/// <see cref="AttachmentsPermissionKeys.View"/>; changing needs <see cref="AttachmentsPermissionKeys.Upload"/> or
/// <see cref="AttachmentsPermissionKeys.Remove"/> (each endpoint still requires its own key). Any id other than
/// <see cref="DemoRecord.RecordId"/> is a record that does not exist. Registered in Development only.
/// </summary>
public sealed class DemoRecordAccess(IOrganizationScope scope) : IAttachableRecordAccess
{
    /// <inheritdoc />
    public string RecordType => DemoRecord.RecordType;

    /// <inheritdoc />
    public async Task<bool> CanViewAsync(long recordId, CancellationToken cancellationToken) =>
        recordId == DemoRecord.RecordId && await HoldsAsync(AttachmentsPermissionKeys.View, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> CanChangeAsync(long recordId, CancellationToken cancellationToken) =>
        recordId == DemoRecord.RecordId
        && (await HoldsAsync(AttachmentsPermissionKeys.Upload, cancellationToken) || await HoldsAsync(AttachmentsPermissionKeys.Remove, cancellationToken));

    private async Task<bool> HoldsAsync(string permissionKey, CancellationToken cancellationToken) =>
        (await scope.ScopeForAsync(permissionKey, cancellationToken)).Allowed;
}

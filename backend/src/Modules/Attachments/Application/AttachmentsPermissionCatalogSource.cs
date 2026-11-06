using Starter.Application.Authorization;

namespace Starter.Modules.Attachments.Application;

/// <summary>The Attachments module's share of the permission catalog.</summary>
public sealed class AttachmentsPermissionCatalogSource : IPermissionCatalogSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(Key: AttachmentsPermissionKeys.View, Description: "List attachment types and a record's attachments, and download them (where the record allows it)."),
        new(Key: AttachmentsPermissionKeys.Upload, Description: "Upload files and link them to a record (where the record allows it)."),
        new(Key: AttachmentsPermissionKeys.Remove, Description: "Remove a record's attachments (where the record allows it)."),
        // scaffold:permission-catalog
    ];
}

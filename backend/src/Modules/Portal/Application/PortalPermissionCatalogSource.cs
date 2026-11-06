using Starter.Application.Authorization;

namespace Starter.Modules.Portal.Application;

/// <summary>The Portal module's share of the permission catalog.</summary>
public sealed class PortalPermissionCatalogSource : IPermissionCatalogSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(Key: PortalPermissionKeys.ContentView, Description: "Read the FAQ, service guide, privacy and terms content in the admin editor."),
        new(Key: PortalPermissionKeys.ContentManage, Description: "Replace the sections of the FAQ, service guide, privacy and terms pages."),
        // scaffold:permission-catalog
    ];
}

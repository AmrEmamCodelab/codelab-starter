using Starter.Application.Authorization;

namespace Starter.Modules.Jobs.Application;

/// <summary>The jobs module's share of the permission catalog.</summary>
public sealed class JobsPermissionCatalogSource : IPermissionCatalogSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(Key: JobsPermissionKeys.View, Description: "List the background jobs and their run history."),
        new(Key: JobsPermissionKeys.Run, Description: "Run a background job now."),
        // scaffold:permission-catalog
    ];
}

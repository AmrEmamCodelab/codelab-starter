using Starter.Application.Authorization;

namespace Starter.Modules.Records.Application;

/// <summary>The Records module's share of the permission catalog.</summary>
public sealed class RecordsPermissionCatalogSource : IPermissionCatalogSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(Key: RecordsPermissionKeys.OversightView, Description: "Read every record of every record type, read-only (admin oversight)."),
        // scaffold:permission-catalog
    ];
}

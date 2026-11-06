using Starter.Application.Authorization;

namespace Starter.Modules.Audit.Application;

/// <summary>The Audit module's share of the permission catalog.</summary>
public sealed class AuditPermissionCatalogSource : IPermissionCatalogSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(Key: AuditPermissionKeys.View, Description: "Read the audit trail of every audited table (personal data masked)."),
        // scaffold:permission-catalog
    ];
}

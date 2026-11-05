namespace Starter.Application.Authorization;

/// <summary>
/// A module's share of the permission catalog. Every module that declares permission keys registers one; the Access
/// module aggregates all sources into the catalog, validates it at startup and writes it to the database.
/// </summary>
public interface IPermissionCatalogSource
{
    /// <summary>The permissions this module declares.</summary>
    IReadOnlyCollection<PermissionDefinition> Permissions { get; }
}

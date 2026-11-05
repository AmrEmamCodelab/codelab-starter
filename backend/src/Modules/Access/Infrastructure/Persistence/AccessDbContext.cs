using Microsoft.EntityFrameworkCore;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Infrastructure.Persistence;

/// <summary>
/// The Access module's DbContext, in database schema <see cref="SchemaName"/> with its own migrations history table.
/// Entity configurations live in this namespace subtree and are applied by <see cref="ModuleDbContext"/>.
/// </summary>
public sealed class AccessDbContext(DbContextOptions<AccessDbContext> options) : ModuleDbContext(options), IAccessUnitOfWork
{
    /// <summary>The database schema of every Access table.</summary>
    public const string SchemaName = "access";

    /// <summary>The permission catalog rows.</summary>
    public DbSet<PermissionEntry> Permissions => Set<PermissionEntry>();

    /// <summary>The direct user grants.</summary>
    public DbSet<UserPermissionGrant> UserPermissionGrants => Set<UserPermissionGrant>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;
}

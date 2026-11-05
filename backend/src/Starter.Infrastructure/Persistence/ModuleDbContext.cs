using Microsoft.EntityFrameworkCore;
using Starter.Application.Persistence;

namespace Starter.Infrastructure.Persistence;

/// <summary>
/// The base of every module's DbContext. Each module owns one DbContext in its own database schema, with its own
/// migrations history table in that schema, so a module can be removed with its tables and migrations (ADR-004). The
/// entity configurations in the context's namespace subtree are applied automatically. Enums keep EF's default
/// integer mapping (rule <c>base.enum-stored-as-int</c>).
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options) : DbContext(options), IUnitOfWork
{
    /// <summary>The database schema that holds this module's tables.</summary>
    protected abstract string Schema { get; }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        var contextNamespace = GetType().Namespace ?? string.Empty;
        modelBuilder.ApplyConfigurationsFromAssembly(
            GetType().Assembly,
            type => type.Namespace is { } ns && ns.StartsWith(contextNamespace, StringComparison.Ordinal));
        base.OnModelCreating(modelBuilder);
    }
}

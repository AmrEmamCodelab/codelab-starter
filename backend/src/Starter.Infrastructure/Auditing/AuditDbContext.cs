using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Starter.Infrastructure.Persistence;

namespace Starter.Infrastructure.Auditing;

/// <summary>
/// The kernel audit trail, in the <c>audit</c> schema with its own migrations history. The viewer reads it from DI;
/// <see cref="SqlAuditTrailWriter"/> opens a short-lived one on the connection and transaction of the module context
/// being saved, so the audit rows commit or roll back with the change.
/// </summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : ModuleDbContext(options)
{
    /// <summary>The database schema of the audit trail.</summary>
    public const string SchemaName = "audit";

    /// <summary>The audit rows.</summary>
    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;

    /// <summary>The options of a context on <paramref name="connection"/> (owned by the caller, never disposed here).</summary>
    public static DbContextOptions<AuditDbContext> OptionsOn(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsHistoryTable(PersistenceRegistration.MigrationsHistoryTable, SchemaName))
            .Options;
    }
}

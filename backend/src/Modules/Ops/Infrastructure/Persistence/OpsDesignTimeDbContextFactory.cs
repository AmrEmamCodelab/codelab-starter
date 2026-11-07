using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Starter.Infrastructure.Persistence;

namespace Starter.Modules.Ops.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the context without the host. Design time only: the connection string
/// below is never opened by <c>migrations add</c> and is the local development database (LocalDB, Windows
/// authentication, no secret).
/// </summary>
public sealed class OpsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<OpsDbContext>
{
    /// <summary>The local development database the design-time tools point at.</summary>
    public const string DesignTimeConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=StarterDev;Trusted_Connection=True";

    /// <inheritdoc />
    public OpsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OpsDbContext>()
            .UseSqlServer(
                DesignTimeConnectionString,
                sql => sql.MigrationsHistoryTable(PersistenceRegistration.MigrationsHistoryTable, OpsDbContext.SchemaName))
            .Options;
        return new OpsDbContext(options);
    }
}

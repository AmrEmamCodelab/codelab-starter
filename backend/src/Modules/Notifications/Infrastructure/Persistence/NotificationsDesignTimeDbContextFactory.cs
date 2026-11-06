using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Starter.Infrastructure.Persistence;

namespace Starter.Modules.Notifications.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the context without the host. Design time only: the connection string
/// below is never opened by <c>migrations add</c> and is the local development database (LocalDB, Windows
/// authentication, no secret).
/// </summary>
public sealed class NotificationsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    /// <summary>The local development database the design-time tools point at.</summary>
    public const string DesignTimeConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=StarterDev;Trusted_Connection=True";

    /// <inheritdoc />
    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseSqlServer(
                DesignTimeConnectionString,
                sql => sql.MigrationsHistoryTable(PersistenceRegistration.MigrationsHistoryTable, NotificationsDbContext.SchemaName))
            .Options;
        return new NotificationsDbContext(options);
    }
}

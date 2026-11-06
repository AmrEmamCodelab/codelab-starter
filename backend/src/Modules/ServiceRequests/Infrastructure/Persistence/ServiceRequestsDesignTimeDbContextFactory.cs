using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Starter.Infrastructure.Persistence;

namespace Starter.Modules.ServiceRequests.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build the context without starting the host (no database is opened).</summary>
public sealed class ServiceRequestsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ServiceRequestsDbContext>
{
    /// <summary>A placeholder connection string; design-time commands never connect.</summary>
    public const string DesignTimeConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=StarterDev;Trusted_Connection=True";

    /// <inheritdoc />
    public ServiceRequestsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ServiceRequestsDbContext>()
            .UseSqlServer(
                DesignTimeConnectionString,
                sql => sql.MigrationsHistoryTable(PersistenceRegistration.MigrationsHistoryTable, ServiceRequestsDbContext.SchemaName))
            .Options;
        return new ServiceRequestsDbContext(options);
    }
}

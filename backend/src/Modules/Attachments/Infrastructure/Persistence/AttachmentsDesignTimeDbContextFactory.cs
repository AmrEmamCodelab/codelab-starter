using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Starter.Infrastructure.Persistence;

namespace Starter.Modules.Attachments.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the context without the host. Design time only: the connection string
/// below is never opened by <c>migrations add</c> and is the local development database (LocalDB, Windows
/// authentication, no secret).
/// </summary>
public sealed class AttachmentsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AttachmentsDbContext>
{
    /// <summary>The local development database the design-time tools point at.</summary>
    public const string DesignTimeConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=StarterDev;Trusted_Connection=True";

    /// <inheritdoc />
    public AttachmentsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AttachmentsDbContext>()
            .UseSqlServer(
                DesignTimeConnectionString,
                sql => sql.MigrationsHistoryTable(PersistenceRegistration.MigrationsHistoryTable, AttachmentsDbContext.SchemaName))
            .Options;
        return new AttachmentsDbContext(options);
    }
}

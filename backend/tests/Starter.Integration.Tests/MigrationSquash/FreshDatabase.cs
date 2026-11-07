using Microsoft.EntityFrameworkCore;
using Starter.Infrastructure.Modularity;
using Starter.Infrastructure.Persistence;
using Starter.Integration.Tests.Support;
using Starter.Modules.Attachments.Infrastructure.Persistence; // codelab:module Attachments
using Starter.Modules.Lookups.Infrastructure.Persistence;
using Starter.Modules.Notifications.Infrastructure.Persistence;

namespace Starter.Integration.Tests.MigrationSquash;

/// <summary>
/// A throwaway database <c>StarterIt_&lt;guid&gt;</c> built only from the squashed migrations of the seeded contexts
/// (Lookups, Notifications, Attachments), each with its history table in its own schema as the host applies them. No
/// host and no startup task runs, so every row the tests read came from a migration. Dropped at the end, also when a
/// migration failed.
/// </summary>
public sealed class FreshDatabase : IAsyncLifetime
{
    /// <summary>The database this fixture owns.</summary>
    public string Name { get; } = $"{ModuleNaming.SolutionTokenOf(typeof(Program).Assembly)}{TestSqlServer.DatabaseMarker}{Guid.NewGuid():N}";

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await TestSqlServer.EnsureStartedAsync();
        await TestSqlServer.CreateDatabaseAsync(Name);
        try
        {
            await using var lookups = Lookups();
            await lookups.Database.MigrateAsync();
            await using var notifications = Notifications();
            await notifications.Database.MigrateAsync();
            await using var attachments = Attachments(); // codelab:module Attachments
            await attachments.Database.MigrateAsync(); // codelab:module Attachments
        }
        catch
        {
            await TestSqlServer.DropDatabaseAsync(Name);
            throw;
        }
    }

    /// <inheritdoc />
    public Task DisposeAsync() => TestSqlServer.DropDatabaseAsync(Name);

    /// <summary>A new Lookups context on the database (the caller disposes it).</summary>
    public LookupsDbContext Lookups() => new(Options<LookupsDbContext>(LookupsDbContext.SchemaName));

    /// <summary>A new Notifications context on the database (the caller disposes it).</summary>
    public NotificationsDbContext Notifications() => new(Options<NotificationsDbContext>(NotificationsDbContext.SchemaName));

    // codelab:module-begin Attachments
    /// <summary>A new Attachments context on the database (the caller disposes it).</summary>
    public AttachmentsDbContext Attachments() => new(Options<AttachmentsDbContext>(AttachmentsDbContext.SchemaName));
    // codelab:module-end Attachments

    private DbContextOptions<TContext> Options<TContext>(string schema)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseSqlServer(TestSqlServer.ConnectionStringFor(Name), sql => sql.MigrationsHistoryTable(PersistenceRegistration.MigrationsHistoryTable, schema))
            .Options;
}

/// <summary>The squashed-migration database tests (one database for the collection, run alone like every collection).</summary>
[CollectionDefinition(Name)]
public sealed class FreshDatabaseGroup : ICollectionFixture<FreshDatabase>
{
    /// <summary>The collection name.</summary>
    public const string Name = "migration squash fresh database";
}

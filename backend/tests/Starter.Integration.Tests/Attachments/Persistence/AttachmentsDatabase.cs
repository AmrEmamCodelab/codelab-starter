using Microsoft.EntityFrameworkCore;
using Starter.Infrastructure.Modularity;
using Starter.Infrastructure.Persistence;
using Starter.Integration.Tests.Support;
using Starter.Modules.Attachments.Infrastructure.Persistence;

namespace Starter.Integration.Tests.Attachments.Persistence;

/// <summary>
/// A throwaway database <c>StarterIt_&lt;guid&gt;</c> migrated with the Attachments migrations only (the real
/// migrations, history table in schema <c>attachments</c>, as the host applies them). Dropped at the end, also when a
/// test failed. No host: the persistence tests drive <see cref="AttachmentsDbContext"/> and <see cref="EfAttachmentStore"/>.
/// </summary>
public sealed class AttachmentsDatabase : IAsyncLifetime
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
            await using var context = NewContext();
            await context.Database.MigrateAsync();
        }
        catch
        {
            await TestSqlServer.DropDatabaseAsync(Name);
            throw;
        }
    }

    /// <inheritdoc />
    public Task DisposeAsync() => TestSqlServer.DropDatabaseAsync(Name);

    /// <summary>A new context on the database (the caller disposes it).</summary>
    public AttachmentsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AttachmentsDbContext>()
            .UseSqlServer(
                TestSqlServer.ConnectionStringFor(Name),
                sql => sql.MigrationsHistoryTable(PersistenceRegistration.MigrationsHistoryTable, AttachmentsDbContext.SchemaName))
            .Options);
}

/// <summary>The Attachments persistence tests (one database for the collection, run alone like every collection).</summary>
[CollectionDefinition(Name)]
public sealed class AttachmentsPersistenceGroup : ICollectionFixture<AttachmentsDatabase>
{
    /// <summary>The collection name.</summary>
    public const string Name = "attachments persistence";
}

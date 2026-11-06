using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Starter.Domain.Auditing;
using Starter.Infrastructure.Auditing;
using Starter.Infrastructure.Modularity;
using Starter.Integration.Tests.Support;

namespace Starter.Integration.Tests.Auditing;

/// <summary>
/// Exit criterion: deleting a parent record that has history is refused by SQL Server, because
/// <c>HasRecordHistory</c> maps the history foreign key ON DELETE RESTRICT. A probe context in this test project (a
/// record and its history table) on its own throwaway database <c>StarterIt_&lt;guid&gt;</c>, dropped afterwards.
/// </summary>
[Collection(RecordHistoryGroup.Name)]
public sealed class RecordHistoryRestrictTests : IAsyncLifetime
{
    /// <summary>SQL Server's error number for a statement that conflicts with a constraint (here the FK).</summary>
    private const int ConstraintConflict = 547;

    private const string ProbeAction = "submitted";
    private const string ProbeActor = "Probe Actor";

    private readonly string _database = $"{ModuleNaming.SolutionTokenOf(typeof(Program).Assembly)}{TestSqlServer.DatabaseMarker}{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        await TestSqlServer.EnsureStartedAsync();
        await TestSqlServer.CreateDatabaseAsync(_database);
        await using var context = NewContext();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => TestSqlServer.DropDatabaseAsync(_database);

    [Fact]
    public async Task The_history_foreign_key_is_restrict_in_the_model_and_in_the_database()
    {
        await using var context = NewContext();

        Assert.Empty(RecordHistoryMapping.Violations(context.Model));
        var deleteAction = await TestSqlServer.ScalarAsync(
            _database,
            "SELECT [delete_referential_action_desc] FROM sys.foreign_keys WHERE [parent_object_id] = OBJECT_ID(@table)",
            new Dictionary<string, object> { ["@table"] = ProbeContext.HistoryTable });
        Assert.Equal("NO_ACTION", deleteAction); // SQL Server's form of Restrict: the delete is refused, nothing cascades
    }

    [Fact]
    public async Task Deleting_a_record_with_history_is_refused_by_SQL_and_both_rows_stay()
    {
        var recordId = await SeedRecordAsync(withHistory: true);

        await using var context = NewContext();
        context.Records.Remove(await context.Records.SingleAsync(record => record.Id == recordId));
        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Equal(ConstraintConflict, Assert.IsType<SqlException>(refused.InnerException).Number);
        await using var check = NewContext();
        Assert.True(await check.Records.AnyAsync(record => record.Id == recordId));
        Assert.Equal(1, await check.History.CountAsync(entry => entry.RecordId == recordId));
    }

    [Fact]
    public async Task Deleting_a_record_without_history_succeeds()
    {
        var recordId = await SeedRecordAsync(withHistory: false);

        await using var context = NewContext();
        context.Records.Remove(await context.Records.SingleAsync(record => record.Id == recordId));
        await context.SaveChangesAsync();

        await using var check = NewContext();
        Assert.False(await check.Records.AnyAsync(record => record.Id == recordId));
    }

    private async Task<long> SeedRecordAsync(bool withHistory)
    {
        await using var context = NewContext();
        var record = new ProbeRecord();
        context.Records.Add(record);
        await context.SaveChangesAsync();
        if (withHistory)
        {
            context.History.Add(new ProbeRecordHistory(new RecordHistoryFacts(
                RecordId: record.Id,
                Action: ProbeAction,
                FromStatus: null,
                ToStatus: null,
                Reason: null,
                ActorUserId: null,
                ActorName: ProbeActor,
                At: TimeProvider.System.GetUtcNow())));
            await context.SaveChangesAsync();
        }

        return record.Id;
    }

    private ProbeContext NewContext() =>
        new(new DbContextOptionsBuilder<ProbeContext>().UseSqlServer(TestSqlServer.ConnectionStringFor(_database)).Options);
}

/// <summary>A probe record (the parent of a history).</summary>
public sealed class ProbeRecord
{
    /// <summary>The identity key.</summary>
    public long Id { get; private set; }
}

/// <summary>The probe record's history table.</summary>
public sealed class ProbeRecordHistory : RecordHistoryEntry
{
    private ProbeRecordHistory()
    {
    }

    /// <summary>A new entry.</summary>
    public ProbeRecordHistory(RecordHistoryFacts facts)
        : base(facts)
    {
    }
}

/// <summary>A test-only context: a record and its history mapped with <c>HasRecordHistory</c>.</summary>
public sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
{
    /// <summary>The schema of the probe tables.</summary>
    public const string Schema = "probe";

    /// <summary>The history table, schema-qualified.</summary>
    public const string HistoryTable = Schema + ".ProbeRecordHistory";

    /// <summary>The records.</summary>
    public DbSet<ProbeRecord> Records => Set<ProbeRecord>();

    /// <summary>The history.</summary>
    public DbSet<ProbeRecordHistory> History => Set<ProbeRecordHistory>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<ProbeRecord>(builder =>
        {
            builder.ToTable(nameof(ProbeRecord));
            builder.HasKey(record => record.Id);
        });
        modelBuilder.Entity<ProbeRecordHistory>(builder =>
        {
            builder.ToTable(nameof(ProbeRecordHistory));
            builder.HasRecordHistory<ProbeRecord, ProbeRecordHistory>();
        });
    }
}

/// <summary>The record history tests (their own database, one at a time like every collection).</summary>
[CollectionDefinition(Name)]
public sealed class RecordHistoryGroup
{
    /// <summary>The collection name.</summary>
    public const string Name = "record history";
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Application.Messaging;
using Starter.Infrastructure.Hosting;
using Starter.Infrastructure.Messaging;
using Starter.Infrastructure.Messaging.Migrations;
using Starter.Infrastructure.Persistence;

namespace Starter.Api.Tests.Messaging;

/// <summary>The kernel outbox without a database: the model, its migration, the row mapping and the registration.</summary>
public sealed class OutboxModelTests
{
    [Fact]
    public void The_model_lives_in_the_messaging_schema_and_matches_the_InitialCreate_migration()
    {
        using var context = OutboxContexts.Offline();

        Assert.Equal(OutboxDbContext.SchemaName, context.Model.GetDefaultSchema());
        Assert.Equal(OutboxTableNames.OutboxMessages, context.Model.FindEntityType(typeof(OutboxEntry))!.GetTableName());
        Assert.Equal([OutboxMigrationIds.InitialCreate], context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_status_is_stored_as_an_int_column()
    {
        using var context = OutboxContexts.Offline();
        var status = context.Model.FindEntityType(typeof(OutboxEntry))!.FindProperty(nameof(OutboxEntry.Status))!;

        Assert.Equal(OutboxSqlTypes.Int32Column, status.GetColumnType());
        Assert.Null(status.GetValueConverter());
    }

    [Fact]
    public void The_migration_script_creates_the_table_its_index_and_its_own_history_in_the_messaging_schema()
    {
        using var context = OutboxContexts.Offline();

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains("CREATE TABLE [messaging].[OutboxMessages]", script, StringComparison.Ordinal);
        Assert.Contains($"[{OutboxMigrationIds.OutboxMessagesStatusIndex}]", script, StringComparison.Ordinal);
        Assert.Contains($"[messaging].[{PersistenceRegistration.MigrationsHistoryTable}]", script, StringComparison.Ordinal);
        Assert.Contains("[PayloadJson] nvarchar(max) NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("[NotBefore] datetimeoffset NULL", script, StringComparison.Ordinal);
    }
}

public sealed class EfOutboxTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Enqueue_writes_one_pending_row_stamped_with_the_clock_and_saves_it()
    {
        var saves = new CountingSaveInterceptor();
        await using var context = OutboxContexts.Offline(saves);
        var notBefore = Now.AddMinutes(10);
        var message = new OutboxMessage(OutboxMessageTypes.AccountActivation, """{"ticketId":"t-1"}""", notBefore);

        await new EfOutbox(context, new FixedClock(Now)).EnqueueAsync(message, CancellationToken.None);

        var row = Assert.Single(context.ChangeTracker.Entries<OutboxEntry>()).Entity;
        Assert.Equal(OutboxMessageTypes.AccountActivation, row.Type);
        Assert.Equal("""{"ticketId":"t-1"}""", row.PayloadJson);
        Assert.Equal(OutboxMessageStatus.Pending, row.Status);
        Assert.Equal(Now, row.CreatedAt);
        Assert.Equal(notBefore, row.NotBefore);
        Assert.Equal(1, saves.Count);
    }

    [Theory]
    [InlineData("", "{}")]
    [InlineData("   ", "{}")]
    [InlineData(OutboxMessageTypes.AccountActivation, "")]
    [InlineData(OutboxMessageTypes.AccountActivation, "  ")]
    public async Task Enqueue_refuses_a_blank_type_or_payload_and_writes_nothing(string type, string payload)
    {
        var saves = new CountingSaveInterceptor();
        await using var context = OutboxContexts.Offline(saves);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new EfOutbox(context, new FixedClock(Now)).EnqueueAsync(new OutboxMessage(type, payload, NotBefore: null), CancellationToken.None));

        Assert.Empty(context.ChangeTracker.Entries<OutboxEntry>());
        Assert.Equal(0, saves.Count);
    }

    [Fact]
    public void A_type_longer_than_the_column_is_refused_and_one_at_the_limit_is_accepted()
    {
        var atLimit = new OutboxMessage(new string('t', OutboxLimits.TypeMaxLength), "{}", NotBefore: null);
        var tooLong = atLimit with { Type = new string('t', OutboxLimits.TypeMaxLength + 1) };

        EfOutbox.Validate(atLimit);
        Assert.Throws<ArgumentException>(() => EfOutbox.Validate(tooLong));
    }

    [Fact]
    public void A_null_message_is_refused() =>
        Assert.Throws<ArgumentNullException>(() => EfOutbox.Validate(null!));
}

public sealed class MessagingRegistrationTests
{
    [Fact]
    public void The_kernel_registers_the_outbox_and_announces_its_context_to_the_startup_runner()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStarterKernel(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Starter"] = "Server=(localdb)\\unused;Database=never-opened" })
            .Build());

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IOutbox) && descriptor.ImplementationType == typeof(EfOutbox));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IModuleDatabase) && descriptor.ImplementationType == typeof(ModuleDatabase<OutboxDbContext>));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(OutboxDbContext));
    }
}

/// <summary>Outbox contexts on a SQL Server connection that is never opened.</summary>
internal static class OutboxContexts
{
    private const string NeverOpened = "Server=.;Database=NeverOpened";

    public static OutboxDbContext Offline(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<OutboxDbContext>()
        .UseSqlServer(NeverOpened, sql => sql.MigrationsHistoryTable(PersistenceRegistration.MigrationsHistoryTable, OutboxDbContext.SchemaName))
        .AddInterceptors(interceptors)
        .Options);
}

/// <summary>Counts saves and answers them without touching the database.</summary>
internal sealed class CountingSaveInterceptor : SaveChangesInterceptor
{
    public int Count { get; private set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Count++;
        return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));
    }
}

/// <summary>A clock stopped at one instant.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

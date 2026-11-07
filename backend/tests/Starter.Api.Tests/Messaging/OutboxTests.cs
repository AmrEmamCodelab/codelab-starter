using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
    public void The_model_lives_in_the_messaging_schema_and_matches_the_latest_migration()
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

    [Fact]
    public void The_one_migration_creates_the_drain_columns_and_targets_the_latest_model()
    {
        using var context = OutboxContexts.Offline();
        var migration = new InitialCreate();
        var target = context.GetService<IModelRuntimeInitializer>().Initialize(migration.TargetModel!, designTime: true);

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains("[Attempt] int NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("[LockedUntil] datetimeoffset NULL", script, StringComparison.Ordinal);
        Assert.Contains("[SentAt] datetimeoffset NULL", script, StringComparison.Ordinal);
        Assert.Contains($"[LastError] nvarchar({OutboxColumnLimits.LastErrorMaxLength}) NULL", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER TABLE", script, StringComparison.Ordinal);
        Assert.Empty(context.GetService<IMigrationsModelDiffer>().GetDifferences(
            source: target.GetRelationalModel(), target: context.GetService<IDesignTimeModel>().Model.GetRelationalModel()));
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
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IOutboxDrainRunner) && descriptor.ImplementationType == typeof(OutboxDrainRunner));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IOutboxStore) && descriptor.ImplementationType == typeof(SqlOutboxStore));
    }

    [Fact]
    public void The_outbox_is_on_by_default_and_the_drain_resolves_without_a_database()
    {
        using var provider = KernelProvider(new Dictionary<string, string?>());
        using var scope = provider.CreateScope();

        Assert.True(provider.GetRequiredService<IOptions<OutboxOptions>>().Value.Enabled);
        Assert.Equal(OutboxOptions.DefaultBatchSize, provider.GetRequiredService<IOptions<OutboxOptions>>().Value.BatchSize);
        Assert.IsType<OutboxDrainRunner>(scope.ServiceProvider.GetRequiredService<IOutboxDrainRunner>());
    }

    [Fact]
    public void The_outbox_switch_and_batch_size_bind_from_Messaging_Outbox()
    {
        using var provider = KernelProvider(new Dictionary<string, string?>
        {
            ["Messaging:Outbox:Enabled"] = "false",
            ["Messaging:Outbox:BatchSize"] = "10",
        });

        var options = provider.GetRequiredService<IOptions<OutboxOptions>>().Value;
        Assert.False(options.Enabled);
        Assert.Equal(10, options.BatchSize);
    }

    [Fact]
    public void A_batch_size_out_of_range_refuses_the_options()
    {
        using var provider = KernelProvider(new Dictionary<string, string?> { ["Messaging:Outbox:BatchSize"] = "0" });

        var refused = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<OutboxOptions>>().Value);
        Assert.Contains("Messaging:Outbox:BatchSize", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_handler_registered_twice_is_registered_once_and_two_handlers_stay_two()
    {
        var services = new ServiceCollection();

        services.AddOutboxHandler<RecordingHandler>().AddOutboxHandler<RecordingHandler>().AddOutboxHandler<OtherTypeHandler>();

        Assert.Equal(2, services.Count(descriptor => descriptor.ServiceType == typeof(IOutboxHandler)));
        Assert.All(services.Where(descriptor => descriptor.ServiceType == typeof(IOutboxHandler)), descriptor => Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime));
    }

    private static ServiceProvider KernelProvider(Dictionary<string, string?> settings)
    {
        settings["ConnectionStrings:Starter"] = "Server=(localdb)\\unused;Database=never-opened";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddStarterKernel(configuration);
        return services.BuildServiceProvider();
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

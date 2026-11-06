using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Starter.Application.Jobs;
using Starter.Application.Records;
using Starter.Infrastructure.Startup;
using Starter.Modules.Deadlines.Application;
using Starter.Modules.Deadlines.Domain;
using Starter.Modules.Deadlines.Infrastructure;
using Starter.Modules.Deadlines.Infrastructure.Persistence;
using Starter.Modules.Deadlines.Infrastructure.Persistence.Migrations;
using Starter.Modules.Deadlines.Tests.Fakes;

namespace Starter.Modules.Deadlines.Tests.Infrastructure;

public sealed class DeadlinesModelTests
{
    [Fact]
    public void The_model_lives_in_the_deadlines_schema_and_matches_the_InitialCreate_migration()
    {
        using var context = NewContext();

        Assert.Equal(DeadlinesDbContext.SchemaName, context.Model.GetDefaultSchema());
        Assert.Equal(DeadlinesTableNames.RecordDeadlines, context.Model.FindEntityType(typeof(RecordDeadline))!.GetTableName());
        Assert.Equal([DeadlinesMigrationIds.InitialCreate], context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_status_is_an_int_column_and_the_derived_flags_are_not_mapped()
    {
        using var context = NewContext();
        var entity = context.Model.FindEntityType(typeof(RecordDeadline))!;

        Assert.Equal(DeadlinesSqlTypes.Int32Column, entity.FindProperty(nameof(RecordDeadline.Status))!.GetColumnType());
        Assert.Null(entity.FindProperty(nameof(RecordDeadline.IsOpen)));
        Assert.Null(entity.FindProperty(nameof(RecordDeadline.AwaitsEscalation)));
    }

    [Fact]
    public void The_migration_script_creates_the_table_and_its_filtered_indexes_in_the_deadlines_schema()
    {
        using var context = NewContext();

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains("[deadlines].[RecordDeadlines]", script, StringComparison.Ordinal);
        Assert.Contains($"CREATE UNIQUE INDEX [{DeadlinesTableNames.OneOpenPerRecordIndex}]", script, StringComparison.Ordinal);
        Assert.Contains(DeadlinesTableNames.OpenByDueAtIndex, script, StringComparison.Ordinal);
        Assert.Contains("WHERE [ClosedAt] IS NULL", script, StringComparison.Ordinal);
        Assert.Contains("[deadlines].[__EFMigrationsHistory]", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_design_time_factory_builds_the_context()
    {
        using var context = new DeadlinesDesignTimeDbContextFactory().CreateDbContext([]);

        Assert.Equal(DeadlinesDbContext.SchemaName, context.Model.GetDefaultSchema());
    }

    private static DeadlinesDbContext NewContext() => new(new DbContextOptionsBuilder<DeadlinesDbContext>()
        .UseSqlServer("Server=.;Database=NeverOpened", sql => sql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, DeadlinesDbContext.SchemaName))
        .Options);
}

public sealed class DeadlinesRegistrationTests
{
    [Fact]
    public void The_module_is_named_after_its_assembly() => Assert.Equal("Deadlines", new DeadlinesModule().Name);

    [Theory]
    [InlineData(typeof(IRecordTransitionObserver), typeof(DeadlineTransitionObserver))]
    [InlineData(typeof(IRecordSlaReader), typeof(RecordSlaReader))]
    [InlineData(typeof(IJob), typeof(SlaScanJob))]
    [InlineData(typeof(IStartupTask), typeof(SlaPolicyStartupCheck))]
    [InlineData(typeof(IRecordDeadlineStore), typeof(RecordDeadlineStore))]
    public void Registration_declares_the_kernel_port_implementations(Type service, Type implementation)
    {
        var services = Registered();

        Assert.Single(services, descriptor => descriptor.ServiceType == service && descriptor.ImplementationType == implementation);
    }

    [Fact]
    public void Registering_twice_adds_no_second_observer_reader_or_job()
    {
        var services = Registered();
        services.AddDeadlinesModule(new ConfigurationBuilder().Build());

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRecordTransitionObserver));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRecordSlaReader));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IJob));
    }

    [Fact]
    public void The_SLA_catalog_is_a_singleton_over_every_source()
    {
        var services = Registered();
        services.AddSingleton<ISlaPolicySource>(new PolicySource(TestRecord.ReviewDefinition()));
        using var provider = services.BuildServiceProvider();

        var catalog = provider.GetRequiredService<SlaPolicyCatalog>();

        Assert.Same(catalog, provider.GetRequiredService<SlaPolicyCatalog>());
        Assert.NotNull(catalog.Find(TestRecord.Type, TestRecord.UnderReview));
    }

    private static ServiceCollection Registered()
    {
        var services = new ServiceCollection();
        services.AddDeadlinesModule(new ConfigurationBuilder().Build());
        return services;
    }
}

public sealed class SlaPolicyStartupCheckTests
{
    [Fact]
    public async Task Valid_definitions_pass_in_the_reference_data_phase()
    {
        var check = Check(TestRecord.ReviewDefinition());

        await check.RunAsync(CancellationToken.None);

        Assert.Equal(StartupPhase.ReferenceData, check.Phase);
    }

    [Fact]
    public async Task An_invalid_definition_stops_the_start_and_lists_every_violation()
    {
        var terminal = new SlaDefinition(TestRecord.Type, TestRecord.Closed, TestRecord.ReviewSla, TimeSpan.Zero, TestRecord.EscalationTemplate);

        var failure = await Assert.ThrowsAsync<SlaPolicyInvalidException>(() =>
            Check(TestRecord.ReviewDefinition(), TestRecord.ReviewDefinition(), terminal).RunAsync(CancellationToken.None));

        Assert.Equal(
            [SlaPolicyViolationKind.DuplicateStatus, SlaPolicyViolationKind.TerminalStatus],
            failure.Violations.Select(violation => violation.Kind));
        Assert.Contains(nameof(SlaPolicyViolationKind.TerminalStatus), failure.Message, StringComparison.Ordinal);
    }

    private static SlaPolicyStartupCheck Check(params SlaDefinition[] definitions) =>
        new(new SlaPolicyCatalog([new PolicySource(definitions)]), new FakeWorkflows(TestRecord.Workflow()), NullLogger<SlaPolicyStartupCheck>.Instance);
}

public sealed class SlaEscalationNotifierTests
{
    [Fact]
    public void Without_a_workflow_the_status_key_is_the_status_value()
    {
        var notifier = new SlaEscalationNotifier(new FakeNotifications(), new FakeWorkflows());
        var deadline = RecordDeadline.Open(7, TestRecord.ReviewDefinition(), TestRecord.Start);

        var placeholders = notifier.PlaceholdersFor(deadline, TestRecord.Summary(7) with { Number = null });

        Assert.Equal("3", placeholders[DeadlinesNotifications.StatusKey]);
        Assert.Equal(string.Empty, placeholders[DeadlinesNotifications.RecordNumber]);
        Assert.Equal(
            [DeadlinesNotifications.DueAt, DeadlinesNotifications.RecordNumber, DeadlinesNotifications.StatusKey],
            placeholders.Keys.Order(StringComparer.Ordinal));
    }
}

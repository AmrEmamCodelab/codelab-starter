using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Starter.Modules.Jobs.Domain;
using Starter.Modules.Jobs.Infrastructure.Options;
using Starter.Modules.Jobs.Infrastructure.Persistence;
using Starter.Modules.Jobs.Infrastructure.Persistence.Migrations;

namespace Starter.Modules.Jobs.Tests.Infrastructure;

public sealed class JobsModelTests
{
    [Fact]
    public void The_model_lives_in_the_jobs_schema_and_matches_the_InitialCreate_migration()
    {
        using var context = NewContext();

        Assert.Equal(JobsDbContext.SchemaName, context.Model.GetDefaultSchema());
        Assert.Equal(JobsTableNames.JobRuns, context.Model.FindEntityType(typeof(JobRun))!.GetTableName());
        Assert.Equal([JobsMigrationIds.InitialCreate], context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData(nameof(JobRun.Status))]
    [InlineData(nameof(JobRun.Trigger))]
    public void Enums_are_int_columns_without_a_converter(string property)
    {
        using var context = NewContext();
        var column = context.Model.FindEntityType(typeof(JobRun))!.FindProperty(property)!;

        Assert.Equal(JobsSqlTypes.Int32Column, column.GetColumnType());
        Assert.Null(column.GetValueConverter());
    }

    [Fact]
    public void The_derived_flag_is_not_mapped()
    {
        using var context = NewContext();

        Assert.Null(context.Model.FindEntityType(typeof(JobRun))!.FindProperty(nameof(JobRun.IsFinished)));
    }

    [Fact]
    public void The_migration_script_creates_the_run_table_and_its_index_in_the_jobs_schema()
    {
        using var context = NewContext();

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains("[jobs].[JobRuns]", script, StringComparison.Ordinal);
        Assert.Contains(JobsTableNames.JobKeyIdIndex, script, StringComparison.Ordinal);
        Assert.Contains("[jobs].[__EFMigrationsHistory]", script, StringComparison.Ordinal);
        Assert.DoesNotContain("hangfire", script, StringComparison.OrdinalIgnoreCase);
    }

    private static JobsDbContext NewContext() => new(new DbContextOptionsBuilder<JobsDbContext>()
        .UseSqlServer("Server=.;Database=NeverOpened", sql => sql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, JobsDbContext.SchemaName))
        .Options);
}

public sealed class JobsOptionsTests
{
    [Fact]
    public void The_defaults_run_the_server_in_Riyadh_time_and_are_valid()
    {
        var options = new JobsOptions();

        Assert.True(options.Enabled);
        Assert.True(options.PrepareSchemaIfNecessary);
        Assert.Equal(JobsOptions.DefaultWorkerCount, options.WorkerCount);
        Assert.Equal("Arab Standard Time", options.TimeZoneId);
        Assert.Equal(TimeSpan.FromHours(3), options.ResolveTimeZone().BaseUtcOffset);
        Assert.True(new JobsOptionsValidator().Validate(name: null, options).Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(JobsOptions.MaxWorkerCount + 1)]
    public void A_worker_count_out_of_range_is_refused(int workers) =>
        Assert.True(new JobsOptionsValidator().Validate(name: null, new JobsOptions { WorkerCount = workers }).Failed);

    [Theory]
    [InlineData("")]
    [InlineData("Mars/Olympus")]
    public void An_unknown_time_zone_is_refused(string timeZoneId) =>
        Assert.True(new JobsOptionsValidator().Validate(name: null, new JobsOptions { TimeZoneId = timeZoneId }).Failed);

    [Fact]
    public void From_reads_the_section_and_keeps_defaults_for_missing_keys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jobs:Enabled"] = "false" })
            .Build();

        var options = JobsOptions.From(configuration);

        Assert.False(options.Enabled);
        Assert.Equal(JobsOptions.DefaultWorkerCount, options.WorkerCount);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Modules.Users.Application.Import;
using Starter.Modules.Users.Domain;
using Starter.Modules.Users.Infrastructure.Options;
using Starter.Modules.Users.Infrastructure.Persistence;
using Starter.Modules.Users.Infrastructure.Persistence.Migrations;

namespace Starter.Modules.Users.Tests.Infrastructure;

public sealed class ActivationOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://localhost:4280")]
    [InlineData("https://portal.example.test/app")]
    public void Unset_or_an_absolute_http_origin_is_valid(string? linkBase) =>
        Assert.True(new ActivationOptionsValidator().Validate(name: null, new ActivationOptions { LinkBaseUrl = linkBase }).Succeeded);

    [Theory]
    [InlineData("localhost:4280")]
    [InlineData("/activate")]
    [InlineData("ftp://portal.example.test")]
    [InlineData("https://portal.example.test/?x=1")]
    [InlineData("https://portal.example.test/#frag")]
    public void Anything_else_fails_at_start(string linkBase) =>
        Assert.True(new ActivationOptionsValidator().Validate(name: null, new ActivationOptions { LinkBaseUrl = linkBase }).Failed);

    [Fact]
    public void The_settings_carry_the_base_or_nothing()
    {
        Assert.False(new ActivationOptions().ToSettings().IsConfigured);
        Assert.Equal(new Uri("http://localhost:4280"), new ActivationOptions { LinkBaseUrl = "http://localhost:4280" }.ToSettings().LinkBase);
    }
}

public sealed class ImportOptionsTests
{
    [Fact]
    public void The_defaults_are_valid_and_map_to_settings()
    {
        var options = new ImportOptions();

        Assert.True(new ImportOptionsValidator().Validate(name: null, options).Succeeded);
        Assert.Equal(ImportLimits.DefaultMaxRows, options.ToSettings().MaxRows);
        Assert.Equal(TimeSpan.FromMinutes(ImportLimits.DefaultBatchLifetimeMinutes), options.ToSettings().BatchLifetime);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(ImportLimits.MaxRowsCeiling + 1, 30)]
    [InlineData(10, 0)]
    [InlineData(10, ImportLimits.MaxBatchLifetimeMinutes + 1)]
    public void Values_outside_the_ceilings_fail_at_start(int maxRows, int lifetimeMinutes) =>
        Assert.True(new ImportOptionsValidator().Validate(name: null, new ImportOptions { MaxRows = maxRows, BatchLifetimeMinutes = lifetimeMinutes }).Failed);
}

public sealed class UsersModelTests
{
    [Fact]
    public void The_model_lives_in_the_users_schema_and_matches_the_InitialCreate_migration()
    {
        using var context = NewContext();

        Assert.Equal(UsersDbContext.SchemaName, context.Model.GetDefaultSchema());
        Assert.Equal(UsersTableNames.Organizations, context.Model.FindEntityType(typeof(Organization))!.GetTableName());
        Assert.Equal(UsersTableNames.ImportBatches, context.Model.FindEntityType(typeof(ImportBatch))!.GetTableName());
        Assert.Equal([UsersMigrationIds.InitialCreate], context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Enums_are_int_columns_and_the_batch_status_is_the_commit_concurrency_token()
    {
        using var context = NewContext();
        var kind = context.Model.FindEntityType(typeof(Organization))!.FindProperty(nameof(Organization.Kind))!;
        var status = context.Model.FindEntityType(typeof(ImportBatch))!.FindProperty(nameof(ImportBatch.Status))!;

        Assert.Equal(UsersSqlTypes.Int32Column, kind.GetColumnType());
        Assert.Null(kind.GetValueConverter());
        Assert.Equal(UsersSqlTypes.Int32Column, status.GetColumnType());
        Assert.True(status.IsConcurrencyToken);
    }

    [Fact]
    public void The_migration_script_creates_both_tables_and_the_unique_code_index()
    {
        using var context = NewContext();

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains("[users].[Organizations]", script, StringComparison.Ordinal);
        Assert.Contains("[users].[ImportBatches]", script, StringComparison.Ordinal);
        Assert.Contains($"CREATE UNIQUE INDEX [{UsersMigrationIds.OrganizationsCodeIndex}]", script, StringComparison.Ordinal);
        Assert.Contains("[users].[__EFMigrationsHistory]", script, StringComparison.Ordinal);
    }

    private static UsersDbContext NewContext() => new(new DbContextOptionsBuilder<UsersDbContext>()
        .UseSqlServer("Server=.;Database=NeverOpened", sql => sql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, UsersDbContext.SchemaName))
        .Options);
}

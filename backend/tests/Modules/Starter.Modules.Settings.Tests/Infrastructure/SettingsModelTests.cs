using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Application.Settings;
using Starter.Modules.Settings.Api;
using Starter.Modules.Settings.Domain;
using Starter.Modules.Settings.Infrastructure.Persistence;
using Starter.Modules.Settings.Infrastructure.Persistence.Migrations;
using Starter.Modules.Settings.Tests.Fakes;

namespace Starter.Modules.Settings.Tests.Infrastructure;

public sealed class SettingsModelTests
{
    [Fact]
    public void The_model_lives_in_the_settings_schema_and_matches_the_InitialCreate_migration()
    {
        using var context = NewContext();

        Assert.Equal(SettingsDbContext.SchemaName, context.Model.GetDefaultSchema());
        Assert.Equal(SettingsTableNames.Settings, context.Model.FindEntityType(typeof(Setting))!.GetTableName());
        Assert.Equal(SettingsTableNames.SettingsStamp, context.Model.FindEntityType(typeof(SettingsStamp))!.GetTableName());
        Assert.Equal([SettingsMigrationIds.InitialCreate], context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_value_type_is_an_int_column_and_the_range_is_not_mapped()
    {
        using var context = NewContext();
        var entity = context.Model.FindEntityType(typeof(Setting))!;
        var valueType = entity.FindProperty(nameof(Setting.ValueType))!;

        Assert.Equal(SettingsSqlTypes.Int32Column, valueType.GetColumnType());
        Assert.Null(valueType.GetValueConverter());
        Assert.Null(entity.FindProperty(nameof(Setting.Range)));
        Assert.Equal(nameof(Setting.Key), Assert.Single(entity.FindPrimaryKey()!.Properties).Name);
    }

    [Fact]
    public void The_migration_script_creates_both_tables_seeds_the_stamp_and_every_setting()
    {
        using var context = NewContext();

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains("[settings].[Settings]", script, StringComparison.Ordinal);
        Assert.Contains("[settings].[SettingsStamp]", script, StringComparison.Ordinal);
        Assert.Contains("[settings].[__EFMigrationsHistory]", script, StringComparison.Ordinal);
        Assert.All(SettingsSeed.Rows, row => Assert.Contains($"N'{row.Key}'", script, StringComparison.Ordinal));
    }

    private static SettingsDbContext NewContext() => new(new DbContextOptionsBuilder<SettingsDbContext>()
        .UseSqlServer("Server=.;Database=NeverOpened", sql => sql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, SettingsDbContext.SchemaName))
        .Options);
}

public sealed class SettingsSeedTests
{
    [Fact]
    public void Every_seeded_default_fits_its_type_and_range() =>
        Assert.All(SettingsSeed.Rows, row => Assert.Equal(row.DefaultValue, Setting.Define(row).Value));

    [Fact]
    public void The_seed_is_exactly_the_platform_setting_keys()
    {
        var declared = typeof(SettingKeys).GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(declared, SettingsSeed.Rows.Select(row => row.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Keys_are_unique_fit_the_column_and_start_with_their_group()
    {
        Assert.Equal(SettingsSeed.Rows.Count, SettingsSeed.Rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(SettingsSeed.Rows, row =>
        {
            Assert.True(row.Key.Length <= SettingFieldLimits.KeyMaxLength, row.Key);
            Assert.StartsWith(row.Group + ".", row.Key, StringComparison.Ordinal);
            Assert.Equal(row.Key.ToLowerInvariant(), row.Key);
            Assert.False(string.IsNullOrWhiteSpace(row.DescriptionAr));
            Assert.False(string.IsNullOrWhiteSpace(row.DescriptionEn));
        });
    }
}

public sealed class SettingResponseTests
{
    [Fact]
    public void The_wire_shape_carries_the_contract_fields_with_the_type_as_an_integer()
    {
        var setting = SampleSettings.Integer();
        setting.Change("30", new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.FromHours(3)), byUserId: 1);

        var json = JsonSerializer.Serialize(SettingResponse.From(setting), new JsonOptions().SerializerOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(
            ["defaultValue", "descriptionAr", "descriptionEn", "group", "key", "updatedAt", "value", "valueType"],
            root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal((int)SettingValueType.WholeNumber, root.GetProperty("valueType").GetInt32());
        Assert.Equal("30", root.GetProperty("value").GetString());
        Assert.Equal("2026-11-06T09:00:00+00:00", root.GetProperty("updatedAt").GetString());
    }

    [Fact]
    public void A_never_changed_setting_has_a_null_update_time() =>
        Assert.Null(SettingResponse.From(SampleSettings.Text()).UpdatedAt);

    [Fact]
    public void A_list_keeps_its_order() =>
        Assert.Equal(
            SampleSettings.All().Select(setting => setting.Key),
            SettingResponse.ListFrom(SampleSettings.All()).Select(response => response.Key));
}

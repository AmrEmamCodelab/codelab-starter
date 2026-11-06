using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;
using Starter.Modules.Settings.Domain;

namespace Starter.Modules.Settings.Infrastructure.Persistence.Migrations;

/// <summary>
/// The Settings schema: the settings table with the seeded settings (<see cref="SettingsSeed"/>) and the single-row
/// version stamp (<see cref="SettingsStamp.SingletonId"/>, version <see cref="SettingsStamp.InitialVersion"/>). Written
/// in the reshaped migration style of the other modules; <see cref="SettingsMigrationModel"/> is its target model and
/// the snapshot, and the model test proves the context has no pending change against it. See MODULE.md.
/// </summary>
[DbContext(typeof(SettingsDbContext))]
[Migration(SettingsMigrationIds.InitialCreate)]
public sealed class InitialCreate : Migration
{
    private static readonly string[] SettingColumns =
    [
        nameof(Setting.Key), nameof(Setting.Group), nameof(Setting.ValueType), nameof(Setting.Value), nameof(Setting.DefaultValue),
        nameof(Setting.DescriptionAr), nameof(Setting.DescriptionEn), nameof(Setting.MinValue), nameof(Setting.MaxValue),
    ];

    private static readonly string[] StampColumns = [nameof(SettingsStamp.Id), nameof(SettingsStamp.Version)];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.EnsureSchema(name: SettingsDbContext.SchemaName);
        CreateSettings(migrationBuilder);
        CreateStamp(migrationBuilder);
        migrationBuilder.InsertData(
            table: SettingsTableNames.SettingsStamp,
            schema: SettingsDbContext.SchemaName,
            columns: StampColumns,
            values: [SettingsStamp.SingletonId, SettingsStamp.InitialVersion]);
        SeedSettings(migrationBuilder);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: SettingsTableNames.SettingsStamp, schema: SettingsDbContext.SchemaName);
        migrationBuilder.DropTable(name: SettingsTableNames.Settings, schema: SettingsDbContext.SchemaName);
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => SettingsMigrationModel.Build(modelBuilder);

    private static void CreateSettings(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: SettingsTableNames.Settings,
            schema: SettingsDbContext.SchemaName,
            columns: table => new
            {
                Key = Text(table, SettingFieldLimits.KeyMaxLength),
                Group = Text(table, SettingFieldLimits.GroupMaxLength),
                ValueType = table.Column<int>(type: SettingsSqlTypes.Int32Column, nullable: false),
                Value = Text(table, SettingFieldLimits.ValueMaxLength),
                DefaultValue = Text(table, SettingFieldLimits.ValueMaxLength),
                DescriptionAr = Text(table, SettingFieldLimits.DescriptionMaxLength),
                DescriptionEn = Text(table, SettingFieldLimits.DescriptionMaxLength),
                MinValue = table.Column<int>(type: SettingsSqlTypes.Int32Column, nullable: true),
                MaxValue = table.Column<int>(type: SettingsSqlTypes.Int32Column, nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: SettingsSqlTypes.DateTimeOffset, nullable: true),
                UpdatedByUserId = table.Column<long>(type: SettingsSqlTypes.BigInt, nullable: true),
            },
            constraints: table => table.PrimaryKey(SettingsMigrationIds.SettingsPrimaryKey, setting => setting.Key));

    private static void CreateStamp(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: SettingsTableNames.SettingsStamp,
            schema: SettingsDbContext.SchemaName,
            columns: table => new
            {
                Id = table.Column<int>(type: SettingsSqlTypes.Int32Column, nullable: false),
                Version = table.Column<long>(type: SettingsSqlTypes.BigInt, nullable: false),
            },
            constraints: table => table.PrimaryKey(SettingsMigrationIds.SettingsStampPrimaryKey, stamp => stamp.Id));

    private static void SeedSettings(MigrationBuilder migrationBuilder)
    {
        foreach (var setting in SettingsSeed.Rows.Select(Setting.Define))
        {
            migrationBuilder.InsertData(
                table: SettingsTableNames.Settings,
                schema: SettingsDbContext.SchemaName,
                columns: SettingColumns,
                values:
                [
                    setting.Key, setting.Group, (int)setting.ValueType, setting.Value, setting.DefaultValue,
                    setting.DescriptionAr, setting.DescriptionEn, setting.MinValue, setting.MaxValue,
                ]);
        }
    }

    private static OperationBuilder<AddColumnOperation> Text(ColumnsBuilder table, int maxLength) =>
        table.Column<string>(type: SettingsSqlTypes.NVarChar(maxLength), maxLength: maxLength, nullable: false);
}

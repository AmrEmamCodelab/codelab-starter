using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Modules.Settings.Domain;

namespace Starter.Modules.Settings.Infrastructure.Persistence.Migrations;

/// <summary>The migration identifiers and constraint names of the Settings schema.</summary>
public static class SettingsMigrationIds
{
    /// <summary>The id of the first migration.</summary>
    public const string InitialCreate = "20261106090000_InitialCreate";

    /// <summary>The primary key of <c>settings.Settings</c>.</summary>
    public const string SettingsPrimaryKey = "PK_Settings";

    /// <summary>The primary key of <c>settings.SettingsStamp</c>.</summary>
    public const string SettingsStampPrimaryKey = "PK_SettingsStamp";
}

/// <summary>The SQL Server store types and annotations the Settings migrations write.</summary>
public static class SettingsSqlTypes
{
    /// <summary>32-bit integer.</summary>
    public const string Int32Column = "int";

    /// <summary>64-bit integer.</summary>
    public const string BigInt = "bigint";

    /// <summary>Date, time and offset.</summary>
    public const string DateTimeOffset = "datetimeoffset";

    /// <summary>The model annotation that records the EF Core version.</summary>
    public const string ProductVersionAnnotation = "ProductVersion";

    /// <summary>The EF Core version of the model.</summary>
    public const string ProductVersion = "10.0.14";

    /// <summary>The longest SQL Server identifier.</summary>
    public const int MaxIdentifierLength = 128;

    /// <summary>A Unicode string column of <paramref name="length"/> characters.</summary>
    public static string NVarChar(int length) => $"nvarchar({length})";
}

/// <summary>
/// The Settings model as of the latest migration: the target model of <see cref="InitialCreate"/> and the model snapshot
/// that <c>dotnet ef migrations add</c> diffs against. A later migration freezes its own copy before this one changes.
/// </summary>
public static class SettingsMigrationModel
{
    /// <summary>Builds the model.</summary>
    public static void Build(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder
            .HasDefaultSchema(SettingsDbContext.SchemaName)
            .HasAnnotation(SettingsSqlTypes.ProductVersionAnnotation, SettingsSqlTypes.ProductVersion)
            .HasAnnotation(RelationalAnnotationNames.MaxIdentifierLength, SettingsSqlTypes.MaxIdentifierLength);
        modelBuilder.UseIdentityColumns();
        modelBuilder.Entity(typeof(Setting).FullName!, BuildSetting);
        modelBuilder.Entity(typeof(SettingsStamp).FullName!, BuildStamp);
    }

    private static void BuildSetting(EntityTypeBuilder entity)
    {
        StringColumn(entity, nameof(Setting.Key), SettingFieldLimits.KeyMaxLength);
        StringColumn(entity, nameof(Setting.DefaultValue), SettingFieldLimits.ValueMaxLength);
        StringColumn(entity, nameof(Setting.DescriptionAr), SettingFieldLimits.DescriptionMaxLength);
        StringColumn(entity, nameof(Setting.DescriptionEn), SettingFieldLimits.DescriptionMaxLength);
        StringColumn(entity, nameof(Setting.Group), SettingFieldLimits.GroupMaxLength);
        entity.Property<int?>(nameof(Setting.MaxValue)).HasColumnType(SettingsSqlTypes.Int32Column);
        entity.Property<int?>(nameof(Setting.MinValue)).HasColumnType(SettingsSqlTypes.Int32Column);
        entity.Property<DateTimeOffset?>(nameof(Setting.UpdatedAt)).HasColumnType(SettingsSqlTypes.DateTimeOffset);
        entity.Property<long?>(nameof(Setting.UpdatedByUserId)).HasColumnType(SettingsSqlTypes.BigInt);
        StringColumn(entity, nameof(Setting.Value), SettingFieldLimits.ValueMaxLength);
        entity.Property<int>(nameof(Setting.ValueType)).HasColumnType(SettingsSqlTypes.Int32Column);
        entity.HasKey(nameof(Setting.Key));
        entity.ToTable(SettingsTableNames.Settings, SettingsDbContext.SchemaName);
    }

    private static void BuildStamp(EntityTypeBuilder entity)
    {
        entity.Property<int>(nameof(SettingsStamp.Id)).HasColumnType(SettingsSqlTypes.Int32Column);
        entity.Property<long>(nameof(SettingsStamp.Version)).HasColumnType(SettingsSqlTypes.BigInt);
        entity.HasKey(nameof(SettingsStamp.Id));
        entity.ToTable(SettingsTableNames.SettingsStamp, SettingsDbContext.SchemaName);
    }

    private static void StringColumn(EntityTypeBuilder entity, string name, int maxLength) =>
        entity.Property<string>(name).IsRequired().HasMaxLength(maxLength).HasColumnType(SettingsSqlTypes.NVarChar(maxLength));
}

/// <summary>The model snapshot <c>dotnet ef migrations add</c> diffs the next migration against.</summary>
[DbContext(typeof(SettingsDbContext))]
public sealed class SettingsDbContextModelSnapshot : ModelSnapshot
{
    /// <inheritdoc />
    protected override void BuildModel(ModelBuilder modelBuilder) => SettingsMigrationModel.Build(modelBuilder);
}

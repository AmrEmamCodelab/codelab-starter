using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Domain.Lookups;
using Starter.Modules.Lookups.Domain;

namespace Starter.Modules.Lookups.Infrastructure.Persistence.Migrations;

/// <summary>The migration identifiers and constraint names of the Lookups schema.</summary>
public static class LookupsMigrationIds
{
    /// <summary>The id of the only migration since the v1.0 squash (the tables and the seeded types); never changed.</summary>
    public const string InitialCreate = "20261106090000_InitialCreate";

    /// <summary>The primary key of <c>lookups.LookupTypes</c>.</summary>
    public const string LookupTypesPrimaryKey = "PK_LookupTypes";

    /// <summary>The primary key of <c>lookups.LookupItems</c> (TypeKey, Id).</summary>
    public const string LookupItemsPrimaryKey = "PK_LookupItems";

    /// <summary>The Restrict foreign key from a value to its type.</summary>
    public const string LookupItemsTypeForeignKey = "FK_LookupItems_LookupTypes_TypeKey";

    /// <summary>The unique index on (TypeKey, Code).</summary>
    public const string LookupItemsTypeCodeIndex = "IX_LookupItems_TypeKey_Code";
}

/// <summary>The SQL Server store types and annotations the Lookups migrations write.</summary>
public static class LookupsSqlTypes
{
    /// <summary>32-bit integer.</summary>
    public const string Int32Column = "int";

    /// <summary>Boolean.</summary>
    public const string Bit = "bit";

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
/// The Lookups model as of the latest migration: the target model of every Lookups migration and the model snapshot
/// that <c>dotnet ef migrations add</c> diffs against. Seed migrations add rows only, so they share it; a later schema
/// migration freezes its own copy before this one changes. It carries no data (rows live in the migrations only).
/// </summary>
public static class LookupsMigrationModel
{
    /// <summary>Builds the model.</summary>
    public static void Build(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder
            .HasDefaultSchema(LookupsDbContext.SchemaName)
            .HasAnnotation(LookupsSqlTypes.ProductVersionAnnotation, LookupsSqlTypes.ProductVersion)
            .HasAnnotation(RelationalAnnotationNames.MaxIdentifierLength, LookupsSqlTypes.MaxIdentifierLength);
        modelBuilder.UseIdentityColumns();
        modelBuilder.Entity(typeof(LookupType).FullName!, BuildLookupType);
        modelBuilder.Entity(typeof(LookupItem).FullName!, BuildLookupItem);
        modelBuilder.Entity(typeof(LookupItem).FullName!, item => item
            .HasOne(typeof(LookupType).FullName!, navigationName: null)
            .WithMany()
            .HasForeignKey(nameof(LookupItem.TypeKey))
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired());
    }

    private static void BuildLookupType(EntityTypeBuilder entity)
    {
        StringColumn(entity, nameof(LookupType.Key), LookupLimits.TypeKeyMaxLength);
        StringColumn(entity, nameof(LookupType.NameAr), LookupLimits.NameMaxLength);
        StringColumn(entity, nameof(LookupType.NameEn), LookupLimits.NameMaxLength);
        entity.HasKey(nameof(LookupType.Key));
        entity.ToTable(LookupsTableNames.LookupTypes, LookupsDbContext.SchemaName);
    }

    private static void BuildLookupItem(EntityTypeBuilder entity)
    {
        StringColumn(entity, nameof(LookupItem.TypeKey), LookupLimits.TypeKeyMaxLength);
        entity.Property<int>(nameof(LookupItem.Id)).HasColumnType(LookupsSqlTypes.Int32Column);
        StringColumn(entity, nameof(LookupItem.Code), LookupLimits.CodeMaxLength);
        entity.Property<bool>(nameof(LookupItem.IsActive)).HasColumnType(LookupsSqlTypes.Bit);
        StringColumn(entity, nameof(LookupItem.NameAr), LookupLimits.NameMaxLength);
        StringColumn(entity, nameof(LookupItem.NameEn), LookupLimits.NameMaxLength);
        entity.Property<int>(nameof(LookupItem.SortOrder)).HasColumnType(LookupsSqlTypes.Int32Column);
        entity.HasKey(nameof(LookupItem.TypeKey), nameof(LookupItem.Id));
        entity.HasIndex(nameof(LookupItem.TypeKey), nameof(LookupItem.Code)).IsUnique();
        entity.ToTable(LookupsTableNames.LookupItems, LookupsDbContext.SchemaName);
    }

    private static void StringColumn(EntityTypeBuilder entity, string name, int maxLength) =>
        entity.Property<string>(name).IsRequired().HasMaxLength(maxLength).HasColumnType(LookupsSqlTypes.NVarChar(maxLength));
}

/// <summary>The model snapshot <c>dotnet ef migrations add</c> diffs the next migration against.</summary>
[DbContext(typeof(LookupsDbContext))]
public sealed class LookupsDbContextModelSnapshot : ModelSnapshot
{
    /// <inheritdoc />
    protected override void BuildModel(ModelBuilder modelBuilder) => LookupsMigrationModel.Build(modelBuilder);
}

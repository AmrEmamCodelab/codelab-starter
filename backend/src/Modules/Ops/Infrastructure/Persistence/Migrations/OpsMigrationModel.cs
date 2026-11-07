using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Modules.Ops.Domain;

namespace Starter.Modules.Ops.Infrastructure.Persistence.Migrations;

/// <summary>The migration identifiers and constraint names of the ops schema.</summary>
public static class OpsMigrationIds
{
    /// <summary>The id of the first migration.</summary>
    public const string InitialCreate = "20261107120000_InitialCreate";

    /// <summary>The primary key of <c>ops.OperatorAccess</c>.</summary>
    public const string OperatorAccessPrimaryKey = "PK_OperatorAccess";

    /// <summary>The primary key of <c>ops.ReferenceCodeSightings</c>.</summary>
    public const string ReferenceCodeSightingsPrimaryKey = "PK_ReferenceCodeSightings";
}

/// <summary>The SQL Server store types and annotations the ops migrations write.</summary>
public static class OpsSqlTypes
{
    /// <summary>32-bit integer.</summary>
    public const string Int32Column = "int";

    /// <summary>64-bit integer.</summary>
    public const string BigInt = "bigint";

    /// <summary>Date, time and offset.</summary>
    public const string DateTimeOffset = "datetimeoffset";

    /// <summary>The SQL Server identity annotation.</summary>
    public const string IdentityAnnotation = "SqlServer:Identity";

    /// <summary>Identity seed 1, increment 1.</summary>
    public const string IdentitySeedIncrement = "1, 1";

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
/// The ops model as of the latest migration: the target model of <see cref="InitialCreate"/> and the model snapshot that
/// <c>dotnet ef migrations add</c> diffs against. A later migration freezes its own copy before this one changes.
/// </summary>
public static class OpsMigrationModel
{
    /// <summary>Builds the model.</summary>
    public static void Build(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder
            .HasDefaultSchema(OpsDbContext.SchemaName)
            .HasAnnotation(OpsSqlTypes.ProductVersionAnnotation, OpsSqlTypes.ProductVersion)
            .HasAnnotation(RelationalAnnotationNames.MaxIdentifierLength, OpsSqlTypes.MaxIdentifierLength);
        modelBuilder.UseIdentityColumns();
        modelBuilder.Entity(typeof(OperatorAccess).FullName!, BuildOperatorAccess);
        modelBuilder.Entity(typeof(ReferenceCodeSighting).FullName!, BuildReferenceCodeSighting);
    }

    private static void BuildOperatorAccess(EntityTypeBuilder entity)
    {
        entity.Property<long>(nameof(OperatorAccess.Id)).ValueGeneratedOnAdd().HasColumnType(OpsSqlTypes.BigInt).UseIdentityColumn();
        entity.Property<DateTimeOffset>(nameof(OperatorAccess.At)).HasColumnType(OpsSqlTypes.DateTimeOffset);
        StringColumn(entity, nameof(OperatorAccess.CallerService), OpsLimits.ServiceNameMaxLength, required: true);
        StringColumn(entity, nameof(OperatorAccess.CorrelationId), OpsLimits.CorrelationIdMaxLength, required: false);
        entity.Property<int>(nameof(OperatorAccess.Endpoint)).HasColumnType(OpsSqlTypes.Int32Column);
        StringColumn(entity, nameof(OperatorAccess.Operator), OpsLimits.OperatorMaxLength, required: true);
        StringColumn(entity, nameof(OperatorAccess.Subject), OpsLimits.SubjectMaxLength, required: false);
        entity.HasKey(nameof(OperatorAccess.Id));
        entity.HasIndex(nameof(OperatorAccess.At)).HasDatabaseName(OpsTableNames.OperatorAccessAtIndex);
        entity.ToTable(OpsTableNames.OperatorAccess, OpsDbContext.SchemaName);
    }

    private static void BuildReferenceCodeSighting(EntityTypeBuilder entity)
    {
        entity.Property<long>(nameof(ReferenceCodeSighting.Id)).ValueGeneratedOnAdd().HasColumnType(OpsSqlTypes.BigInt).UseIdentityColumn();
        StringColumn(entity, nameof(ReferenceCodeSighting.CorrelationId), OpsLimits.CorrelationIdMaxLength, required: true);
        StringColumn(entity, nameof(ReferenceCodeSighting.Method), OpsLimits.MethodMaxLength, required: true);
        entity.Property<DateTimeOffset>(nameof(ReferenceCodeSighting.OccurredAt)).HasColumnType(OpsSqlTypes.DateTimeOffset);
        StringColumn(entity, nameof(ReferenceCodeSighting.Path), OpsLimits.PathMaxLength, required: true);
        StringColumn(entity, nameof(ReferenceCodeSighting.ReferenceCode), OpsLimits.ReferenceCodeLength, required: true);
        entity.Property<int>(nameof(ReferenceCodeSighting.StatusCode)).HasColumnType(OpsSqlTypes.Int32Column);
        entity.HasKey(nameof(ReferenceCodeSighting.Id));
        entity.HasIndex(nameof(ReferenceCodeSighting.OccurredAt)).HasDatabaseName(OpsTableNames.OccurredAtIndex);
        entity.HasIndex(nameof(ReferenceCodeSighting.ReferenceCode), nameof(ReferenceCodeSighting.Id)).HasDatabaseName(OpsTableNames.ReferenceCodeIdIndex);
        entity.ToTable(OpsTableNames.ReferenceCodeSightings, OpsDbContext.SchemaName);
    }

    private static void StringColumn(EntityTypeBuilder entity, string name, int maxLength, bool required) =>
        entity.Property<string>(name).IsRequired(required).HasMaxLength(maxLength).HasColumnType(OpsSqlTypes.NVarChar(maxLength));
}

/// <summary>The model snapshot <c>dotnet ef migrations add</c> diffs the next migration against.</summary>
[DbContext(typeof(OpsDbContext))]
public sealed class OpsDbContextModelSnapshot : ModelSnapshot
{
    /// <inheritdoc />
    protected override void BuildModel(ModelBuilder modelBuilder) => OpsMigrationModel.Build(modelBuilder);
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;
using Starter.Modules.ServiceRequests.Domain;

namespace Starter.Modules.ServiceRequests.Infrastructure.Persistence.Migrations;

/// <summary>
/// The ServiceRequests schema: one row of the sample's own fields per record, keyed by the Records kernel's record id
/// (by value; no foreign key across schemas). Written in the reshaped migration style of the other modules;
/// <see cref="ServiceRequestsMigrationModel"/> is its target model and the snapshot, and the model test proves the
/// context has no pending change against it.
/// </summary>
[DbContext(typeof(ServiceRequestsDbContext))]
[Migration(ServiceRequestsMigrationIds.InitialCreate)]
public sealed class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.EnsureSchema(name: ServiceRequestsDbContext.SchemaName);
        migrationBuilder.CreateTable(
            name: ServiceRequestsTableNames.ServiceRequests,
            schema: ServiceRequestsDbContext.SchemaName,
            columns: table => new
            {
                RecordId = table.Column<long>(type: ServiceRequestsSqlTypes.BigInt, nullable: false),
                CategoryId = table.Column<int>(type: ServiceRequestsSqlTypes.Int32Column, nullable: true),
                Title = Text(table, ServiceRequestsLimits.TitleMaxLength),
                Description = Text(table, ServiceRequestsLimits.DescriptionMaxLength),
                ContactPhone = Text(table, ServiceRequestsLimits.ContactPhoneLength),
                PreferredDate = table.Column<DateOnly>(type: ServiceRequestsSqlTypes.Date, nullable: true),
            },
            constraints: table => table.PrimaryKey(ServiceRequestsMigrationIds.ServiceRequestsPrimaryKey, request => request.RecordId));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: ServiceRequestsTableNames.ServiceRequests, schema: ServiceRequestsDbContext.SchemaName);
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ServiceRequestsMigrationModel.Build(modelBuilder);

    private static OperationBuilder<AddColumnOperation> Text(ColumnsBuilder table, int maxLength) =>
        table.Column<string>(type: ServiceRequestsSqlTypes.NVarChar(maxLength), maxLength: maxLength, nullable: true);
}

/// <summary>The migration identifiers and constraint names of the ServiceRequests schema.</summary>
public static class ServiceRequestsMigrationIds
{
    /// <summary>The id of the first migration.</summary>
    public const string InitialCreate = "20261106160000_InitialCreate";

    /// <summary>The primary key of <c>servicerequests.ServiceRequests</c>.</summary>
    public const string ServiceRequestsPrimaryKey = "PK_ServiceRequests";
}

/// <summary>The SQL Server store types and annotations the ServiceRequests migrations write.</summary>
public static class ServiceRequestsSqlTypes
{
    /// <summary>32-bit integer.</summary>
    public const string Int32Column = "int";

    /// <summary>64-bit integer.</summary>
    public const string BigInt = "bigint";

    /// <summary>A calendar date without time.</summary>
    public const string Date = "date";

    /// <summary>The EF Core version annotation name.</summary>
    public const string ProductVersionAnnotation = "ProductVersion";

    /// <summary>The EF Core version the model was written with.</summary>
    public const string ProductVersion = "10.0.14";

    /// <summary>The longest SQL Server identifier.</summary>
    public const int MaxIdentifierLength = 128;

    /// <summary>Bounded Unicode text.</summary>
    public static string NVarChar(int length) => $"nvarchar({length})";
}

/// <summary>The ServiceRequests model as the migrations know it (the target model of every migration and the snapshot).</summary>
public static class ServiceRequestsMigrationModel
{
    /// <summary>Builds the model.</summary>
    public static void Build(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder
            .HasDefaultSchema(ServiceRequestsDbContext.SchemaName)
            .HasAnnotation(ServiceRequestsSqlTypes.ProductVersionAnnotation, ServiceRequestsSqlTypes.ProductVersion)
            .HasAnnotation(RelationalAnnotationNames.MaxIdentifierLength, ServiceRequestsSqlTypes.MaxIdentifierLength);
        modelBuilder.UseIdentityColumns();
        modelBuilder.Entity(typeof(ServiceRequest).FullName!, BuildServiceRequest);
    }

    private static void BuildServiceRequest(EntityTypeBuilder entity)
    {
        entity.Property<long>(nameof(ServiceRequest.RecordId)).HasColumnType(ServiceRequestsSqlTypes.BigInt);
        entity.Property<int?>(nameof(ServiceRequest.CategoryId)).HasColumnType(ServiceRequestsSqlTypes.Int32Column);
        Text(entity, nameof(ServiceRequest.ContactPhone), ServiceRequestsLimits.ContactPhoneLength);
        Text(entity, nameof(ServiceRequest.Description), ServiceRequestsLimits.DescriptionMaxLength);
        entity.Property<DateOnly?>(nameof(ServiceRequest.PreferredDate)).HasColumnType(ServiceRequestsSqlTypes.Date);
        Text(entity, nameof(ServiceRequest.Title), ServiceRequestsLimits.TitleMaxLength);
        entity.HasKey(nameof(ServiceRequest.RecordId));
        entity.ToTable(ServiceRequestsTableNames.ServiceRequests, ServiceRequestsDbContext.SchemaName);
    }

    private static void Text(EntityTypeBuilder entity, string name, int maxLength) =>
        entity.Property<string>(name).HasMaxLength(maxLength).HasColumnType(ServiceRequestsSqlTypes.NVarChar(maxLength));
}

/// <summary>The model snapshot <c>dotnet ef migrations add</c> diffs the next migration against.</summary>
[DbContext(typeof(ServiceRequestsDbContext))]
public sealed class ServiceRequestsDbContextModelSnapshot : ModelSnapshot
{
    /// <inheritdoc />
    protected override void BuildModel(ModelBuilder modelBuilder) => ServiceRequestsMigrationModel.Build(modelBuilder);
}

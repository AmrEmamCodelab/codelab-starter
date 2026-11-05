using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Modules.Users.Domain;

namespace Starter.Modules.Users.Infrastructure.Persistence.Migrations;

/// <summary>The migration identifiers and constraint names of the Users schema.</summary>
public static class UsersMigrationIds
{
    /// <summary>The id of the first migration.</summary>
    public const string InitialCreate = "20261105120000_InitialCreate";

    /// <summary>The primary key of <c>users.Organizations</c>.</summary>
    public const string OrganizationsPrimaryKey = "PK_Organizations";

    /// <summary>The unique index on the organization code.</summary>
    public const string OrganizationsCodeIndex = "IX_Organizations_Code";

    /// <summary>The primary key of <c>users.ImportBatches</c>.</summary>
    public const string ImportBatchesPrimaryKey = "PK_ImportBatches";

    /// <summary>The index on the batch expiry (for the later clean-up job).</summary>
    public const string ImportBatchesExpiresAtIndex = "IX_ImportBatches_ExpiresAt";
}

/// <summary>The SQL Server store types and annotations the Users migrations write.</summary>
public static class UsersSqlTypes
{
    /// <summary>32-bit integer.</summary>
    public const string Int32Column = "int";

    /// <summary>64-bit integer.</summary>
    public const string BigInt = "bigint";

    /// <summary>Boolean.</summary>
    public const string Bit = "bit";

    /// <summary>GUID.</summary>
    public const string UniqueIdentifier = "uniqueidentifier";

    /// <summary>Date, time and offset.</summary>
    public const string DateTimeOffset = "datetimeoffset";

    /// <summary>Unbounded Unicode text.</summary>
    public const string NVarCharMax = "nvarchar(max)";

    /// <summary>The identity column annotation.</summary>
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
/// The Users model as of the latest migration: the target model of <see cref="InitialCreate"/> and the model snapshot
/// that <c>dotnet ef migrations add</c> diffs against. A later migration freezes its own copy before this one changes.
/// </summary>
public static class UsersMigrationModel
{
    private const string IdColumn = nameof(Organization.Id);

    /// <summary>Builds the model.</summary>
    public static void Build(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder
            .HasDefaultSchema(UsersDbContext.SchemaName)
            .HasAnnotation(UsersSqlTypes.ProductVersionAnnotation, UsersSqlTypes.ProductVersion)
            .HasAnnotation(RelationalAnnotationNames.MaxIdentifierLength, UsersSqlTypes.MaxIdentifierLength);
        modelBuilder.UseIdentityColumns();
        modelBuilder.Entity(typeof(Organization).FullName!, BuildOrganization);
        modelBuilder.Entity(typeof(ImportBatch).FullName!, BuildImportBatch);
    }

    private static void BuildOrganization(EntityTypeBuilder entity)
    {
        IdentityColumn(entity);
        StringColumn(entity, nameof(Organization.Code), UsersLimits.OrganizationCodeMaxLength);
        entity.Property<DateTimeOffset>(nameof(Organization.CreatedAt)).HasColumnType(UsersSqlTypes.DateTimeOffset);
        entity.Property<bool>(nameof(Organization.IsActive)).HasColumnType(UsersSqlTypes.Bit);
        entity.Property<int>(nameof(Organization.Kind)).HasColumnType(UsersSqlTypes.Int32Column);
        StringColumn(entity, nameof(Organization.NameAr), UsersLimits.OrganizationNameMaxLength);
        StringColumn(entity, nameof(Organization.NameEn), UsersLimits.OrganizationNameMaxLength);
        entity.Property<DateTimeOffset?>(nameof(Organization.UpdatedAt)).HasColumnType(UsersSqlTypes.DateTimeOffset);
        entity.HasKey(IdColumn);
        entity.HasIndex(nameof(Organization.Code)).IsUnique();
        entity.ToTable(UsersTableNames.Organizations, UsersDbContext.SchemaName);
    }

    private static void BuildImportBatch(EntityTypeBuilder entity)
    {
        IdentityColumn(entity);
        entity.Property<DateTimeOffset?>(nameof(ImportBatch.CommittedAt)).HasColumnType(UsersSqlTypes.DateTimeOffset);
        entity.Property<DateTimeOffset>(nameof(ImportBatch.CreatedAt)).HasColumnType(UsersSqlTypes.DateTimeOffset);
        entity.Property<Guid>(nameof(ImportBatch.CreatedBySubjectId)).HasColumnType(UsersSqlTypes.UniqueIdentifier);
        entity.Property<DateTimeOffset>(nameof(ImportBatch.ExpiresAt)).HasColumnType(UsersSqlTypes.DateTimeOffset);
        entity.Property<int>(nameof(ImportBatch.RowCount)).HasColumnType(UsersSqlTypes.Int32Column);
        entity.Property<string>(nameof(ImportBatch.RowsJson)).IsRequired().HasColumnType(UsersSqlTypes.NVarCharMax);
        entity.Property<int>(nameof(ImportBatch.Status)).IsConcurrencyToken().HasColumnType(UsersSqlTypes.Int32Column);
        entity.Property<int>(nameof(ImportBatch.ValidRowCount)).HasColumnType(UsersSqlTypes.Int32Column);
        entity.HasKey(IdColumn);
        entity.HasIndex(nameof(ImportBatch.ExpiresAt));
        entity.ToTable(UsersTableNames.ImportBatches, UsersDbContext.SchemaName);
    }

    private static void IdentityColumn(EntityTypeBuilder entity) =>
        entity.Property<long>(IdColumn).ValueGeneratedOnAdd().HasColumnType(UsersSqlTypes.BigInt).UseIdentityColumn();

    private static void StringColumn(EntityTypeBuilder entity, string name, int maxLength) =>
        entity.Property<string>(name).IsRequired().HasMaxLength(maxLength).HasColumnType(UsersSqlTypes.NVarChar(maxLength));
}

/// <summary>The model snapshot <c>dotnet ef migrations add</c> diffs the next migration against.</summary>
[DbContext(typeof(UsersDbContext))]
public sealed class UsersDbContextModelSnapshot : ModelSnapshot
{
    /// <inheritdoc />
    protected override void BuildModel(ModelBuilder modelBuilder) => UsersMigrationModel.Build(modelBuilder);
}

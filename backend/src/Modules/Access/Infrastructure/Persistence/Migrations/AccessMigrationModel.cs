using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Modules.Access.Domain;
using Starter.Modules.Access.Infrastructure.Persistence.Configurations;

namespace Starter.Modules.Access.Infrastructure.Persistence.Migrations;

/// <summary>The migration identifiers and constraint names of the Access schema.</summary>
public static class AccessMigrationIds
{
    /// <summary>The id of the first migration.</summary>
    public const string InitialCreate = "20261104203731_InitialCreate";

    /// <summary>The primary key of <c>access.Permissions</c>.</summary>
    public const string PermissionsPrimaryKey = "PK_Permissions";

    /// <summary>The unique index on the permission key.</summary>
    public const string PermissionsKeyIndex = "IX_Permissions_Key";

    /// <summary>The primary key of <c>access.UserPermissionGrants</c>.</summary>
    public const string UserPermissionGrantsPrimaryKey = "PK_UserPermissionGrants";

    /// <summary>The grant to permission foreign key.</summary>
    public const string UserPermissionGrantsPermissionForeignKey = "FK_UserPermissionGrants_Permissions_PermissionId";

    /// <summary>The index on the granted permission.</summary>
    public const string UserPermissionGrantsPermissionIndex = "IX_UserPermissionGrants_PermissionId";

    /// <summary>The unique (user, permission) index.</summary>
    public const string UserPermissionGrantsUserPermissionIndex = "IX_UserPermissionGrants_UserId_PermissionId";
}

/// <summary>The SQL Server store types and annotations the Access migrations write.</summary>
public static class SqlTypes
{
    /// <summary>64-bit integer.</summary>
    public const string BigInt = "bigint";

    /// <summary>Boolean.</summary>
    public const string Bit = "bit";

    /// <summary>Date, time and offset.</summary>
    public const string DateTimeOffset = "datetimeoffset";

    /// <summary>The identity column annotation.</summary>
    public const string IdentityAnnotation = "SqlServer:Identity";

    /// <summary>Identity seed 1, increment 1.</summary>
    public const string IdentitySeedIncrement = "1, 1";

    /// <summary>The model annotation that records the EF Core version.</summary>
    public const string ProductVersionAnnotation = "ProductVersion";

    /// <summary>The EF Core version that scaffolded the model.</summary>
    public const string ProductVersion = "10.0.14";

    /// <summary>The longest SQL Server identifier.</summary>
    public const int MaxIdentifierLength = 128;

    /// <summary>A Unicode string column of <paramref name="length"/> characters.</summary>
    public static string NVarChar(int length) => $"nvarchar({length})";
}

/// <summary>
/// The Access model as of the latest migration: the target model of <see cref="InitialCreate"/> and the model snapshot
/// that <c>dotnet ef migrations add</c> diffs against. A later migration freezes its own copy before this one changes.
/// </summary>
public static class AccessMigrationModel
{
    private const string IdColumn = nameof(PermissionEntry.Id);

    /// <summary>Builds the model.</summary>
    public static void Build(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder
            .HasDefaultSchema(AccessDbContext.SchemaName)
            .HasAnnotation(SqlTypes.ProductVersionAnnotation, SqlTypes.ProductVersion)
            .HasAnnotation(RelationalAnnotationNames.MaxIdentifierLength, SqlTypes.MaxIdentifierLength);
        modelBuilder.UseIdentityColumns();
        modelBuilder.Entity(typeof(PermissionEntry).FullName!, BuildPermission);
        modelBuilder.Entity(typeof(UserPermissionGrant).FullName!, BuildUserPermissionGrant);
    }

    private static void BuildPermission(EntityTypeBuilder entity)
    {
        entity.Property<long>(IdColumn).ValueGeneratedOnAdd().HasColumnType(SqlTypes.BigInt).UseIdentityColumn();
        StringColumn(entity, nameof(PermissionEntry.Description), AccessLimits.PermissionDescriptionMaxLength);
        entity.Property<bool>(nameof(PermissionEntry.IsObsolete)).HasColumnType(SqlTypes.Bit);
        StringColumn(entity, nameof(PermissionEntry.Key), AccessLimits.PermissionKeyMaxLength);
        StringColumn(entity, nameof(PermissionEntry.Owner), AccessLimits.PermissionOwnerMaxLength);
        entity.HasKey(IdColumn);
        entity.HasIndex(nameof(PermissionEntry.Key)).IsUnique();
        entity.ToTable(AccessTableNames.Permissions, AccessDbContext.SchemaName);
    }

    private static void BuildUserPermissionGrant(EntityTypeBuilder entity)
    {
        entity.Property<long>(IdColumn).ValueGeneratedOnAdd().HasColumnType(SqlTypes.BigInt).UseIdentityColumn();
        entity.Property<DateTimeOffset>(nameof(UserPermissionGrant.GrantedAt)).HasColumnType(SqlTypes.DateTimeOffset);
        entity.Property<long>(nameof(UserPermissionGrant.PermissionId)).HasColumnType(SqlTypes.BigInt);
        entity.Property<long>(nameof(UserPermissionGrant.UserId)).HasColumnType(SqlTypes.BigInt);
        entity.HasKey(IdColumn);
        entity.HasIndex(nameof(UserPermissionGrant.PermissionId));
        entity.HasIndex(nameof(UserPermissionGrant.UserId), nameof(UserPermissionGrant.PermissionId)).IsUnique();
        entity.ToTable(AccessTableNames.UserPermissionGrants, AccessDbContext.SchemaName);
        entity.HasOne(typeof(PermissionEntry).FullName!, nameof(UserPermissionGrant.Permission))
            .WithMany()
            .HasForeignKey(nameof(UserPermissionGrant.PermissionId))
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        entity.Navigation(nameof(UserPermissionGrant.Permission));
    }

    private static void StringColumn(EntityTypeBuilder entity, string name, int maxLength) =>
        entity.Property<string>(name).IsRequired().HasMaxLength(maxLength).HasColumnType(SqlTypes.NVarChar(maxLength));
}

/// <summary>The model snapshot <c>dotnet ef migrations add</c> diffs the next migration against.</summary>
[DbContext(typeof(AccessDbContext))]
public sealed class AccessDbContextModelSnapshot : ModelSnapshot
{
    /// <inheritdoc />
    protected override void BuildModel(ModelBuilder modelBuilder) => AccessMigrationModel.Build(modelBuilder);
}

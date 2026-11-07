using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Modules.Access.Domain;
using Starter.Modules.Access.Infrastructure.Persistence.Configurations;

namespace Starter.Modules.Access.Infrastructure.Persistence.Migrations;

/// <summary>
/// The Access model as of the latest migration: the target model of <see cref="InitialCreate"/> and the model snapshot
/// that <c>dotnet ef migrations add</c> diffs against. A later migration first freezes a copy of this model as the
/// target model of <see cref="InitialCreate"/> (<c>InitialCreateModel</c>), then changes this one.
/// </summary>
public static class AccessMigrationModel
{
    private const string IdColumn = nameof(PermissionEntry.Id);
    private static readonly string PermissionEntity = typeof(PermissionEntry).FullName!;
    private static readonly string RoleEntity = typeof(Role).FullName!;

    /// <summary>Builds the model.</summary>
    public static void Build(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder
            .HasDefaultSchema(AccessDbContext.SchemaName)
            .HasAnnotation(SqlTypes.ProductVersionAnnotation, SqlTypes.ProductVersion)
            .HasAnnotation(RelationalAnnotationNames.MaxIdentifierLength, SqlTypes.MaxIdentifierLength);
        modelBuilder.UseIdentityColumns();
        modelBuilder.Entity(PermissionEntity, BuildPermission);
        modelBuilder.Entity(RoleEntity, BuildRole);
        modelBuilder.Entity(typeof(RolePermissionEntry).FullName!, BuildRolePermission);
        modelBuilder.Entity(typeof(UserPermissionGrant).FullName!, BuildUserPermissionGrant);
        modelBuilder.Entity(typeof(UserRoleAssignment).FullName!, BuildUserRoleAssignment);
        modelBuilder.Entity(RoleEntity, entity => entity.Navigation(nameof(Role.Permissions)));
    }

    private static void BuildPermission(EntityTypeBuilder entity)
    {
        IdentityKey(entity);
        StringColumn(entity, nameof(PermissionEntry.Description), AccessLimits.PermissionDescriptionMaxLength);
        entity.Property<bool>(nameof(PermissionEntry.IsObsolete)).HasColumnType(SqlTypes.Bit);
        StringColumn(entity, nameof(PermissionEntry.Key), AccessLimits.PermissionKeyMaxLength);
        StringColumn(entity, nameof(PermissionEntry.Owner), AccessLimits.PermissionOwnerMaxLength);
        entity.HasKey(IdColumn);
        entity.HasIndex(nameof(PermissionEntry.Key)).IsUnique();
        entity.ToTable(AccessTableNames.Permissions, AccessDbContext.SchemaName);
    }

    private static void BuildRole(EntityTypeBuilder entity)
    {
        IdentityKey(entity);
        StringColumn(entity, nameof(Role.Code), AccessLimits.RoleCodeMaxLength);
        entity.Property<DateTimeOffset>(nameof(Role.CreatedAt)).HasColumnType(SqlTypes.DateTimeOffset);
        entity.Property<bool>(nameof(Role.IsActive)).HasColumnType(SqlTypes.Bit);
        StringColumn(entity, nameof(Role.NameAr), AccessLimits.RoleNameMaxLength);
        StringColumn(entity, nameof(Role.NameEn), AccessLimits.RoleNameMaxLength);
        entity.Property<DateTimeOffset>(nameof(Role.UpdatedAt)).HasColumnType(SqlTypes.DateTimeOffset);
        entity.Property<int?>(nameof(Role.VirtualKind)).HasColumnType(SqlTypes.EnumColumn);
        entity.HasKey(IdColumn);
        entity.HasIndex(nameof(Role.Code)).IsUnique();
        entity.HasIndex(nameof(Role.NameAr)).IsUnique();
        entity.HasIndex(nameof(Role.NameEn)).IsUnique();
        entity.HasIndex(nameof(Role.VirtualKind)).IsUnique().HasFilter(RoleConfiguration.VirtualKindFilter);
        entity.ToTable(AccessTableNames.Roles, AccessDbContext.SchemaName);
    }

    private static void BuildRolePermission(EntityTypeBuilder entity)
    {
        IdentityKey(entity);
        entity.Property<long>(nameof(RolePermissionEntry.PermissionId)).HasColumnType(SqlTypes.BigInt);
        entity.Property<long>(nameof(RolePermissionEntry.RoleId)).HasColumnType(SqlTypes.BigInt);
        entity.HasKey(IdColumn);
        entity.HasIndex(nameof(RolePermissionEntry.PermissionId));
        entity.HasIndex(nameof(RolePermissionEntry.RoleId), nameof(RolePermissionEntry.PermissionId)).IsUnique();
        entity.ToTable(AccessTableNames.RolePermissions, AccessDbContext.SchemaName);
        entity.HasOne(PermissionEntity, nameof(RolePermissionEntry.Permission))
            .WithMany()
            .HasForeignKey(nameof(RolePermissionEntry.PermissionId))
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        entity.HasOne(RoleEntity, nameof(RolePermissionEntry.Role))
            .WithMany(nameof(Role.Permissions))
            .HasForeignKey(nameof(RolePermissionEntry.RoleId))
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
        entity.Navigation(nameof(RolePermissionEntry.Permission));
        entity.Navigation(nameof(RolePermissionEntry.Role));
    }

    private static void BuildUserPermissionGrant(EntityTypeBuilder entity)
    {
        IdentityKey(entity);
        entity.Property<DateTimeOffset>(nameof(UserPermissionGrant.GrantedAt)).HasColumnType(SqlTypes.DateTimeOffset);
        entity.Property<long>(nameof(UserPermissionGrant.PermissionId)).HasColumnType(SqlTypes.BigInt);
        entity.Property<int>(nameof(UserPermissionGrant.Scope)).HasColumnType(SqlTypes.EnumColumn);
        entity.Property<long>(nameof(UserPermissionGrant.UserId)).HasColumnType(SqlTypes.BigInt);
        entity.HasKey(IdColumn);
        entity.HasIndex(nameof(UserPermissionGrant.PermissionId));
        entity.HasIndex(nameof(UserPermissionGrant.UserId), nameof(UserPermissionGrant.PermissionId)).IsUnique();
        entity.ToTable(AccessTableNames.UserPermissionGrants, AccessDbContext.SchemaName);
        entity.HasOne(PermissionEntity, nameof(UserPermissionGrant.Permission))
            .WithMany()
            .HasForeignKey(nameof(UserPermissionGrant.PermissionId))
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        entity.Navigation(nameof(UserPermissionGrant.Permission));
    }

    private static void BuildUserRoleAssignment(EntityTypeBuilder entity)
    {
        IdentityKey(entity);
        entity.Property<DateTimeOffset>(nameof(UserRoleAssignment.AssignedAt)).HasColumnType(SqlTypes.DateTimeOffset);
        entity.Property<long>(nameof(UserRoleAssignment.RoleId)).HasColumnType(SqlTypes.BigInt);
        entity.Property<int>(nameof(UserRoleAssignment.Scope)).HasColumnType(SqlTypes.EnumColumn);
        entity.Property<long>(nameof(UserRoleAssignment.UserId)).HasColumnType(SqlTypes.BigInt);
        entity.HasKey(IdColumn);
        entity.HasIndex(nameof(UserRoleAssignment.RoleId));
        entity.HasIndex(nameof(UserRoleAssignment.UserId), nameof(UserRoleAssignment.RoleId)).IsUnique();
        entity.ToTable(AccessTableNames.UserRoleAssignments, AccessDbContext.SchemaName);
        entity.HasOne(RoleEntity, nameof(UserRoleAssignment.Role))
            .WithMany()
            .HasForeignKey(nameof(UserRoleAssignment.RoleId))
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        entity.Navigation(nameof(UserRoleAssignment.Role));
    }

    private static void IdentityKey(EntityTypeBuilder entity) =>
        entity.Property<long>(IdColumn).ValueGeneratedOnAdd().HasColumnType(SqlTypes.BigInt).UseIdentityColumn();

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

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Modules.Access.Domain;
using Starter.Modules.Access.Infrastructure.Persistence.Configurations;

namespace Starter.Modules.Access.Infrastructure.Persistence.Migrations;

/// <summary>
/// The FROZEN target model of <see cref="InitialCreate"/> (the Access model before <c>RolesAndGrants</c>). Never
/// change it: EF diffs each migration against its own target model. The latest model is <see cref="AccessMigrationModel"/>.
/// </summary>
public static class InitialCreateModel
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

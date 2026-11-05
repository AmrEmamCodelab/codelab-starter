using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Infrastructure.Persistence.Configurations;

/// <summary>
/// Table <c>access.Roles</c>: unique code, unique English and Arabic names, and at most one role per virtual kind (a
/// filtered unique index). The kind is stored as an int.
/// </summary>
public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    /// <summary>The filter of the unique virtual-kind index (only virtual roles take part).</summary>
    public const string VirtualKindFilter = "[" + nameof(Role.VirtualKind) + "] IS NOT NULL";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(AccessTableNames.Roles);
        builder.HasKey(role => role.Id);
        builder.Property(role => role.Code).HasMaxLength(AccessLimits.RoleCodeMaxLength).IsRequired();
        builder.Property(role => role.NameEn).HasMaxLength(AccessLimits.RoleNameMaxLength).IsRequired();
        builder.Property(role => role.NameAr).HasMaxLength(AccessLimits.RoleNameMaxLength).IsRequired();
        builder.Ignore(role => role.IsVirtual);
        builder.HasIndex(role => role.Code).IsUnique();
        builder.HasIndex(role => role.NameEn).IsUnique();
        builder.HasIndex(role => role.NameAr).IsUnique();
        builder.HasIndex(role => role.VirtualKind).IsUnique().HasFilter(VirtualKindFilter);
        builder.HasMany(role => role.Permissions)
            .WithOne(granted => granted.Role)
            .HasForeignKey(granted => granted.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(role => role.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>Table <c>access.RolePermissions</c>: one row per (role, permission), unique.</summary>
public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermissionEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RolePermissionEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(AccessTableNames.RolePermissions);
        builder.HasKey(granted => granted.Id);
        builder.HasOne(granted => granted.Permission)
            .WithMany()
            .HasForeignKey(granted => granted.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(granted => new { granted.RoleId, granted.PermissionId }).IsUnique();
    }
}

/// <summary>
/// Table <c>access.UserRoleAssignments</c>: one row per (user, role), unique, with its scope (an int). The role is
/// restricted (roles are never deleted); the user id has no foreign key (ADR-004).
/// </summary>
public sealed class UserRoleAssignmentConfiguration : IEntityTypeConfiguration<UserRoleAssignment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserRoleAssignment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(AccessTableNames.UserRoleAssignments);
        builder.HasKey(assignment => assignment.Id);
        builder.Property(assignment => assignment.Scope);
        builder.HasOne(assignment => assignment.Role)
            .WithMany()
            .HasForeignKey(assignment => assignment.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(assignment => new { assignment.UserId, assignment.RoleId }).IsUnique();
    }
}

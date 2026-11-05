using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Infrastructure.Persistence.Configurations;

/// <summary>
/// Table <c>access.UserPermissionGrants</c>: one row per (user, permission), unique, with its scope (an int). The user
/// id has no foreign key, because users live in another module's schema (ADR-004).
/// </summary>
public sealed class UserPermissionGrantConfiguration : IEntityTypeConfiguration<UserPermissionGrant>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserPermissionGrant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(AccessTableNames.UserPermissionGrants);
        builder.HasKey(grant => grant.Id);
        builder.Property(grant => grant.Scope);
        builder.HasOne(grant => grant.Permission)
            .WithMany()
            .HasForeignKey(grant => grant.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(grant => new { grant.UserId, grant.PermissionId }).IsUnique();
    }
}

/// <summary>The Access table names.</summary>
public static class AccessTableNames
{
    /// <summary>The permission catalog.</summary>
    public const string Permissions = "Permissions";

    /// <summary>The direct user grants.</summary>
    public const string UserPermissionGrants = "UserPermissionGrants";

    /// <summary>The roles.</summary>
    public const string Roles = "Roles";

    /// <summary>The permissions of each role.</summary>
    public const string RolePermissions = "RolePermissions";

    /// <summary>The roles assigned to users.</summary>
    public const string UserRoleAssignments = "UserRoleAssignments";
}

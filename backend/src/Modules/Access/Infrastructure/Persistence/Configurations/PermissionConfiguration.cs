using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>access.Permissions</c>: one row per catalog key, unique by key.</summary>
public sealed class PermissionConfiguration : IEntityTypeConfiguration<PermissionEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PermissionEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(AccessTableNames.Permissions);
        builder.HasKey(permission => permission.Id);
        builder.Property(permission => permission.Key).HasMaxLength(AccessLimits.PermissionKeyMaxLength).IsRequired();
        builder.Property(permission => permission.Owner).HasMaxLength(AccessLimits.PermissionOwnerMaxLength).IsRequired();
        builder.Property(permission => permission.Description).HasMaxLength(AccessLimits.PermissionDescriptionMaxLength).IsRequired();
        builder.HasIndex(permission => permission.Key).IsUnique();
    }
}

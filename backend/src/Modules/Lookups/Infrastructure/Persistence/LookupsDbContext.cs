using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Domain.Lookups;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Lookups.Application;
using Starter.Modules.Lookups.Domain;

namespace Starter.Modules.Lookups.Infrastructure.Persistence;

/// <summary>The Lookups module's tables, in the <c>lookups</c> schema: the lookup types and their values.</summary>
public sealed class LookupsDbContext(DbContextOptions<LookupsDbContext> options) : ModuleDbContext(options), ILookupsUnitOfWork
{
    /// <summary>The database schema of the module.</summary>
    public const string SchemaName = "lookups";

    /// <summary>The lookup types.</summary>
    public DbSet<LookupType> LookupTypes => Set<LookupType>();

    /// <summary>The lookup values of every type.</summary>
    public DbSet<LookupItem> LookupItems => Set<LookupItem>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;
}

/// <summary>The <c>lookups.LookupTypes</c> table: the key is the type key itself (no surrogate).</summary>
public sealed class LookupTypeConfiguration : IEntityTypeConfiguration<LookupType>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<LookupType> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(LookupsTableNames.LookupTypes);
        builder.HasKey(type => type.Key);
        builder.Property(type => type.Key).HasMaxLength(LookupLimits.TypeKeyMaxLength).ValueGeneratedNever();
        builder.Property(type => type.NameAr).HasMaxLength(LookupLimits.NameMaxLength).IsRequired();
        builder.Property(type => type.NameEn).HasMaxLength(LookupLimits.NameMaxLength).IsRequired();
    }
}

/// <summary>
/// The <c>lookups.LookupItems</c> table: key (TypeKey, Id) with the id never generated (a <c>&lt;Name&gt;Ids</c>
/// constant), a unique code per type, and a Restrict foreign key to the type (a type with values cannot be deleted).
/// </summary>
public sealed class LookupItemConfiguration : IEntityTypeConfiguration<LookupItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<LookupItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(LookupsTableNames.LookupItems);
        builder.HasKey(item => new { item.TypeKey, item.Id });
        builder.Property(item => item.TypeKey).HasMaxLength(LookupLimits.TypeKeyMaxLength);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Code).HasMaxLength(LookupLimits.CodeMaxLength).IsRequired();
        builder.Property(item => item.NameAr).HasMaxLength(LookupLimits.NameMaxLength).IsRequired();
        builder.Property(item => item.NameEn).HasMaxLength(LookupLimits.NameMaxLength).IsRequired();
        builder.HasIndex(item => new { item.TypeKey, item.Code }).IsUnique();
        builder.HasOne<LookupType>().WithMany().HasForeignKey(item => item.TypeKey).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>The Lookups table names.</summary>
public static class LookupsTableNames
{
    /// <summary>The lookup types table.</summary>
    public const string LookupTypes = "LookupTypes";

    /// <summary>The lookup values table.</summary>
    public const string LookupItems = "LookupItems";
}

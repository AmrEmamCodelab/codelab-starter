using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Ops.Domain;

namespace Starter.Modules.Ops.Infrastructure.Persistence;

/// <summary>The Ops module's tables, in the <c>ops</c> schema: the operator audit trail and the reference-code index.</summary>
public sealed class OpsDbContext(DbContextOptions<OpsDbContext> options) : ModuleDbContext(options)
{
    /// <summary>The database schema of the module.</summary>
    public const string SchemaName = "ops";

    /// <summary>The operator calls.</summary>
    public DbSet<OperatorAccess> OperatorAccess => Set<OperatorAccess>();

    /// <summary>The reference-code sightings.</summary>
    public DbSet<ReferenceCodeSighting> ReferenceCodeSightings => Set<ReferenceCodeSighting>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;
}

/// <summary>The <c>ops.OperatorAccess</c> table.</summary>
public sealed class OperatorAccessConfiguration : IEntityTypeConfiguration<OperatorAccess>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OperatorAccess> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(OpsTableNames.OperatorAccess);
        builder.HasKey(access => access.Id);
        builder.Property(access => access.Operator).HasMaxLength(OpsLimits.OperatorMaxLength).IsRequired();
        builder.Property(access => access.CallerService).HasMaxLength(OpsLimits.ServiceNameMaxLength).IsRequired();
        builder.Property(access => access.Subject).HasMaxLength(OpsLimits.SubjectMaxLength);
        builder.Property(access => access.CorrelationId).HasMaxLength(OpsLimits.CorrelationIdMaxLength);
        builder.HasIndex(access => access.At).HasDatabaseName(OpsTableNames.OperatorAccessAtIndex);
    }
}

/// <summary>The <c>ops.ReferenceCodeSightings</c> table.</summary>
public sealed class ReferenceCodeSightingConfiguration : IEntityTypeConfiguration<ReferenceCodeSighting>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ReferenceCodeSighting> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(OpsTableNames.ReferenceCodeSightings);
        builder.HasKey(sighting => sighting.Id);
        builder.Property(sighting => sighting.ReferenceCode).HasMaxLength(OpsLimits.ReferenceCodeLength).IsRequired();
        builder.Property(sighting => sighting.CorrelationId).HasMaxLength(OpsLimits.CorrelationIdMaxLength).IsRequired();
        builder.Property(sighting => sighting.Method).HasMaxLength(OpsLimits.MethodMaxLength).IsRequired();
        builder.Property(sighting => sighting.Path).HasMaxLength(OpsLimits.PathMaxLength).IsRequired();
        builder.HasIndex(sighting => new { sighting.ReferenceCode, sighting.Id }).HasDatabaseName(OpsTableNames.ReferenceCodeIdIndex);
        builder.HasIndex(sighting => sighting.OccurredAt).HasDatabaseName(OpsTableNames.OccurredAtIndex);
    }
}

/// <summary>The Ops table and index names.</summary>
public static class OpsTableNames
{
    /// <summary>The operator audit trail.</summary>
    public const string OperatorAccess = "OperatorAccess";

    /// <summary>The reference-code index.</summary>
    public const string ReferenceCodeSightings = "ReferenceCodeSightings";

    /// <summary>The index behind reading the operator trail by time.</summary>
    public const string OperatorAccessAtIndex = "IX_OperatorAccess_At";

    /// <summary>The index behind "newest sighting of a code".</summary>
    public const string ReferenceCodeIdIndex = "IX_ReferenceCodeSightings_ReferenceCode_Id";

    /// <summary>The index behind the retention purge.</summary>
    public const string OccurredAtIndex = "IX_ReferenceCodeSightings_OccurredAt";
}

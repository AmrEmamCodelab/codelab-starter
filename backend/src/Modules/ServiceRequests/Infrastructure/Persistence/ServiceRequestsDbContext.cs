using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Infrastructure.Persistence;
using Starter.Modules.ServiceRequests.Application;
using Starter.Modules.ServiceRequests.Domain;

namespace Starter.Modules.ServiceRequests.Infrastructure.Persistence;

/// <summary>The sample's database context: schema <see cref="SchemaName"/>, one table of its own fields per record.</summary>
public sealed class ServiceRequestsDbContext(DbContextOptions<ServiceRequestsDbContext> options) : ModuleDbContext(options)
{
    /// <summary>The module's schema (its migrations history lives there too).</summary>
    public const string SchemaName = "servicerequests";

    /// <summary>The rows, one per service request record.</summary>
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;
}

/// <summary>The module's table names.</summary>
public static class ServiceRequestsTableNames
{
    /// <summary>The sample's own fields, keyed by the Records kernel's record id.</summary>
    public const string ServiceRequests = "ServiceRequests";
}

/// <summary>
/// The mapping of <see cref="ServiceRequest"/>: the record id is the key (given, never generated; no foreign key: the
/// record lives in the Records schema), the column sizes come from <see cref="ServiceRequestsLimits"/>.
/// </summary>
public sealed class ServiceRequestConfiguration : IEntityTypeConfiguration<ServiceRequest>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ServiceRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(ServiceRequestsTableNames.ServiceRequests);
        builder.HasKey(request => request.RecordId);
        builder.Property(request => request.RecordId).ValueGeneratedNever();
        builder.Property(request => request.Title).HasMaxLength(ServiceRequestsLimits.TitleMaxLength);
        builder.Property(request => request.Description).HasMaxLength(ServiceRequestsLimits.DescriptionMaxLength);
        builder.Property(request => request.ContactPhone).HasMaxLength(ServiceRequestsLimits.ContactPhoneLength);
    }
}

/// <summary>The EF Core implementation of <see cref="IServiceRequestStore"/>.</summary>
public sealed class EfServiceRequestStore(ServiceRequestsDbContext context) : IServiceRequestStore
{
    /// <inheritdoc />
    public Task<ServiceRequest?> FindAsync(long recordId, CancellationToken cancellationToken) =>
        context.ServiceRequests.FirstOrDefaultAsync(request => request.RecordId == recordId, cancellationToken);

    /// <inheritdoc />
    public void Add(ServiceRequest request) => context.ServiceRequests.Add(request);

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}

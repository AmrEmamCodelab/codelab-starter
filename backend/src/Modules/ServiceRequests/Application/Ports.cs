using Codelab.Security.PublicIds;
using Starter.Modules.ServiceRequests.Domain;

namespace Starter.Modules.ServiceRequests.Application;

/// <summary>The sample's own rows (<c>servicerequests.ServiceRequests</c>); implemented in Infrastructure.</summary>
public interface IServiceRequestStore
{
    /// <summary>The row of record <paramref name="recordId"/>, tracked for an update, or null when none was written yet.</summary>
    Task<ServiceRequest?> FindAsync(long recordId, CancellationToken cancellationToken);

    /// <summary>Adds a new row (saved by <see cref="SaveChangesAsync"/>).</summary>
    void Add(ServiceRequest request);

    /// <summary>Saves the added and changed rows.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Today's calendar date in the app time zone (the Numbering time zone, <c>Numbering:TimeZoneId</c>), so the
/// preferred-date rule and the reference number year agree on the day; implemented in Infrastructure over
/// <see cref="TimeProvider"/>.
/// </summary>
public interface IServiceRequestCalendar
{
    /// <summary>Today in the app time zone.</summary>
    DateOnly Today();
}

/// <summary>
/// The PublicId family of a category value on the wire: the Lookups module's <c>lookup-items</c> family, read with this
/// module's own <see cref="IIdDomain"/> (the Lookups recipe; modules never reference the Lookups project).
/// </summary>
public sealed class ServiceRequestCategoryTokens : IIdDomain
{
    /// <summary>The domain label of every lookup value id (part of the wire format; never rename).</summary>
    public const string DomainLabel = "lookup-items";

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}

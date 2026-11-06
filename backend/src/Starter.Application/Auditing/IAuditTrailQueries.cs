using Starter.Domain.Auditing;

namespace Starter.Application.Auditing;

/// <summary>
/// Reads the data-level audit trail (<c>audit.AuditEntries</c>), newest first, a page at a time. The values it returns
/// are already masked: personal data was masked when the row was written, and secrets were never recorded.
/// </summary>
public interface IAuditTrailQueries
{
    /// <summary>
    /// Up to <see cref="AuditTrailQuery.Take"/> entries matching <paramref name="query"/>, newest first, and whether
    /// more exist after them.
    /// </summary>
    Task<AuditTrailPage> FindAsync(AuditTrailQuery query, CancellationToken cancellationToken);
}

/// <summary>A page request over the audit trail. Every filter is optional.</summary>
/// <param name="EntityType">The audited entity type name (as recorded, e.g. <c>Organization</c>), or null for all.</param>
/// <param name="EntityKey">The entity's key as recorded (needs <paramref name="EntityType"/>), or null for all.</param>
/// <param name="BeforeId">Only entries older than this entry id (the keyset cursor), or null for the newest.</param>
/// <param name="Take">The page size (1 or more; the caller caps it).</param>
public sealed record AuditTrailQuery(string? EntityType, string? EntityKey, long? BeforeId, int Take);

/// <summary>One page of the audit trail.</summary>
/// <param name="Items">The entries, newest first.</param>
/// <param name="HasMore">True when older entries exist after the last one.</param>
public sealed record AuditTrailPage(IReadOnlyList<AuditTrailEntry> Items, bool HasMore);

/// <summary>One audited change of one row.</summary>
/// <param name="Id">The entry's internal id (the keyset cursor).</param>
/// <param name="EntityType">The audited entity type name.</param>
/// <param name="EntityKey">The row's key: the number of a single integer key, the text of any other single key, or a JSON object of the parts of a composite key; null when the entity has no key.</param>
/// <param name="Action">What happened to the row.</param>
/// <param name="Actor">The acting subject id, or <see cref="AuditActors.System"/>.</param>
/// <param name="CorrelationId">The request or job correlation id, or null.</param>
/// <param name="At">When the change was saved.</param>
/// <param name="Changes">The recorded property values (masked where personal).</param>
public sealed record AuditTrailEntry(
    long Id,
    string EntityType,
    string? EntityKey,
    AuditAction Action,
    string Actor,
    string? CorrelationId,
    DateTimeOffset At,
    IReadOnlyList<AuditPropertyChange> Changes);

/// <summary>One recorded property of an audited change.</summary>
/// <param name="Property">The property name.</param>
/// <param name="OldValue">The value before (null for an insert, or when it was null), masked when personal.</param>
/// <param name="NewValue">The value after (null for a delete, or when it is null), masked when personal.</param>
/// <param name="IsIdentifier">True when the value is an internal integer id (the row's key or a foreign key), which an API must not show as a number.</param>
public sealed record AuditPropertyChange(string Property, string? OldValue, string? NewValue, bool IsIdentifier);

/// <summary>The actor names the audit trail records.</summary>
public static class AuditActors
{
    /// <summary>The actor of a change no user made: a background job, a startup task, an anonymous call.</summary>
    public const string System = "SYSTEM";

    /// <summary>The longest recorded actor (a subject id in its 36-character form fits).</summary>
    public const int MaxLength = 64;
}

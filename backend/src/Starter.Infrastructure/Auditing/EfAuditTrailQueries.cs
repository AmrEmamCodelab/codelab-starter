using Microsoft.EntityFrameworkCore;
using Starter.Application.Auditing;

namespace Starter.Infrastructure.Auditing;

/// <summary>
/// <see cref="IAuditTrailQueries"/> on <see cref="AuditDbContext"/>: a keyset page (newest first, <c>Id &lt; before</c>),
/// one row more than asked to tell whether more exist. Read-only (no tracking).
/// </summary>
public sealed class EfAuditTrailQueries(AuditDbContext context) : IAuditTrailQueries
{
    /// <inheritdoc />
    public async Task<AuditTrailPage> FindAsync(AuditTrailQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Take, 1, nameof(query));
        var rows = await Filter(context.Entries.AsNoTracking(), query)
            .OrderByDescending(entry => entry.Id)
            .Take(query.Take + 1)
            .ToListAsync(cancellationToken);
        var items = rows.Take(query.Take).Select(ToEntry).ToList();
        return new AuditTrailPage(items, HasMore: rows.Count > query.Take);
    }

    /// <summary>The rows <paramref name="query"/> selects (the key filter applies only with a type).</summary>
    public static IQueryable<AuditEntry> Filter(IQueryable<AuditEntry> entries, AuditTrailQuery query)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(query);
        if (query.EntityType is { } entityType)
        {
            entries = entries.Where(entry => entry.EntityType == entityType);
            if (query.EntityKey is { } entityKey)
            {
                entries = entries.Where(entry => entry.EntityKey == entityKey);
            }
        }

        return query.BeforeId is { } before ? entries.Where(entry => entry.Id < before) : entries;
    }

    /// <summary>The port's view of <paramref name="row"/>.</summary>
    public static AuditTrailEntry ToEntry(AuditEntry row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new AuditTrailEntry(
            Id: row.Id,
            EntityType: row.EntityType,
            EntityKey: row.EntityKey,
            Action: row.Action,
            Actor: row.Actor,
            CorrelationId: row.CorrelationId,
            At: row.At,
            Changes: AuditChangesJson.Deserialize(row.ChangesJson));
    }
}

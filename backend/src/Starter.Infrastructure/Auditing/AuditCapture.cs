using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Starter.Application.Auditing;
using Starter.Domain.Auditing;

namespace Starter.Infrastructure.Auditing;

/// <summary>
/// The audit rows of one save, taken from the change tracker before the save runs (no query is issued). Modified and
/// deleted rows are built at once, while their original values exist; inserted rows are built by
/// <see cref="Complete"/> after the save, when the database has assigned their keys.
/// </summary>
public sealed class AuditCapture
{
    private readonly List<AuditEntry> _ready;
    private readonly List<KeyValuePair<EntityEntry, AuditEntityPlan>> _added;

    private AuditCapture(List<AuditEntry> ready, List<KeyValuePair<EntityEntry, AuditEntityPlan>> added, AuditStamp stamp)
    {
        _ready = ready;
        _added = added;
        Stamp = stamp;
    }

    /// <summary>Who saved, and when.</summary>
    public AuditStamp Stamp { get; }

    /// <summary>True when the save changes nothing that is audited.</summary>
    public bool IsEmpty => _ready.Count == 0 && _added.Count == 0;

    /// <summary>Captures the audited changes tracked by <paramref name="context"/>.</summary>
    public static AuditCapture Take(DbContext context, AuditStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(stamp);
        var ready = new List<AuditEntry>();
        var added = new List<KeyValuePair<EntityEntry, AuditEntityPlan>>();
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            var plan = AuditPlans.For(entry.Metadata);
            if (!plan.IsAudited)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    added.Add(new(entry, plan));
                    break;
                case EntityState.Modified when Modified(entry, plan, stamp) is { } modified:
                    ready.Add(modified);
                    break;
                case EntityState.Deleted:
                    ready.Add(Deleted(entry, plan, stamp));
                    break;
            }
        }

        return new AuditCapture(ready, added, stamp);
    }

    /// <summary>Every row of the save: the ones built before it, then the inserts with their saved keys and values.</summary>
    public IReadOnlyList<AuditEntry> Complete() =>
        [.. _ready, .. _added.Select(pair => Added(pair.Key, pair.Value, Stamp))];

    private static AuditEntry Added(EntityEntry entry, AuditEntityPlan plan, AuditStamp stamp)
    {
        var changes = Recorded(entry, plan)
            .Select(pair => new AuditPropertyChange(pair.Key.Metadata.Name, OldValue: null, AuditValues.Record(pair.Value, pair.Key.CurrentValue), IsIdentifier(pair.Value)))
            .ToList();
        return Row(entry, plan, AuditAction.Added, changes, stamp);
    }

    private static AuditEntry? Modified(EntityEntry entry, AuditEntityPlan plan, AuditStamp stamp)
    {
        var changes = Recorded(entry, plan)
            .Where(pair => pair.Key.IsModified && !Equals(pair.Key.OriginalValue, pair.Key.CurrentValue))
            .Select(pair => new AuditPropertyChange(
                pair.Key.Metadata.Name,
                AuditValues.Record(pair.Value, pair.Key.OriginalValue),
                AuditValues.Record(pair.Value, pair.Key.CurrentValue),
                IsIdentifier(pair.Value)))
            .ToList();
        return changes.Count == 0 ? null : Row(entry, plan, AuditAction.Modified, changes, stamp);
    }

    private static AuditEntry Deleted(EntityEntry entry, AuditEntityPlan plan, AuditStamp stamp)
    {
        var changes = Recorded(entry, plan)
            .Select(pair => new AuditPropertyChange(pair.Key.Metadata.Name, AuditValues.Record(pair.Value, pair.Key.OriginalValue), NewValue: null, IsIdentifier(pair.Value)))
            .ToList();
        return Row(entry, plan, AuditAction.Deleted, changes, stamp);
    }

    private static IEnumerable<KeyValuePair<PropertyEntry, AuditPropertyPlan>> Recorded(EntityEntry entry, AuditEntityPlan plan) =>
        entry.Properties
            .Select(property => new KeyValuePair<PropertyEntry, AuditPropertyPlan>(property, plan.For(property.Metadata.Name)))
            .Where(pair => pair.Value.Treatment != AuditPropertyTreatment.Excluded);

    private static bool IsIdentifier(AuditPropertyPlan plan) => plan.Treatment == AuditPropertyTreatment.Identifier;

    private static AuditEntry Row(EntityEntry entry, AuditEntityPlan plan, AuditAction action, IReadOnlyList<AuditPropertyChange> changes, AuditStamp stamp) =>
        AuditEntry.From(new AuditEntryFacts(
            EntityType: plan.EntityName,
            EntityKey: AuditKeys.Of(entry, useOriginalValues: action == AuditAction.Deleted),
            Action: action,
            ChangesJson: AuditChangesJson.Serialize(changes),
            Stamp: stamp));
}

namespace Starter.Modules.Attachments.Domain;

/// <summary>
/// The one rule of the start-up synchronisation of declared attachment types (the types owning modules declare through
/// <c>IAttachmentTypeSeedSource</c>) against the stored catalog, matched by key: a missing type is added, a type whose
/// names or rules differ is refreshed, an equal one is left alone. Idempotent: planning again after applying a plan
/// yields nothing to do, and a database that already holds the rows (for example from the shipped seed migration
/// that wrote the sample's types before the seam existed) is never seeded twice.
/// </summary>
public static class AttachmentTypeSeeding
{
    /// <summary>The plan that brings <paramref name="stored"/> to hold every type of <paramref name="declared"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// A declared type matches a stored one by key but not by id or record type, or takes the id of a stored type with
    /// another key (ids and keys are never reused or renumbered): a configuration fault that stops the host.
    /// </exception>
    public static AttachmentTypeSeedPlan Plan(IReadOnlyCollection<ServiceAttachment> stored, IReadOnlyCollection<ServiceAttachmentSeed> declared)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(declared);
        var byKey = stored.ToDictionary(type => type.Key, StringComparer.Ordinal);
        var ids = stored.ToDictionary(type => type.Id);
        var toAdd = new List<ServiceAttachment>();
        var toRefresh = new List<AttachmentTypeRefresh>();
        var conflicts = new List<string>();
        foreach (var seed in declared)
        {
            var wanted = ServiceAttachment.Seeded(seed);
            if (byKey.TryGetValue(wanted.Key, out var existing))
            {
                Compare(existing, wanted, seed, toRefresh, conflicts);
            }
            else if (ids.TryGetValue(wanted.Id, out var holder))
            {
                conflicts.Add($"{wanted.Key} takes id {wanted.Id} of stored type {holder.Key}");
            }
            else
            {
                toAdd.Add(wanted);
            }
        }

        return conflicts.Count == 0
            ? new AttachmentTypeSeedPlan(ToAdd: toAdd, ToRefresh: toRefresh, UnchangedCount: declared.Count - toAdd.Count - toRefresh.Count)
            : throw new InvalidOperationException(
                "Declared attachment types conflict with the stored catalog (ids, keys and record types never change): "
                + string.Join("; ", conflicts) + ".");
    }

    private static void Compare(
        ServiceAttachment existing, ServiceAttachment wanted, ServiceAttachmentSeed seed, List<AttachmentTypeRefresh> toRefresh, List<string> conflicts)
    {
        if (!existing.HasSameIdentityAs(wanted))
        {
            conflicts.Add($"{wanted.Key} is declared as id {wanted.Id} of {wanted.RecordType} but stored as id {existing.Id} of {existing.RecordType}");
            return;
        }

        if (!existing.HasSameContentAs(wanted))
        {
            toRefresh.Add(new AttachmentTypeRefresh(Stored: existing, Declared: seed));
        }
    }
}

/// <summary>What one synchronisation run writes.</summary>
/// <param name="ToAdd">The declared types the catalog does not hold yet, ready to insert.</param>
/// <param name="ToRefresh">The stored types whose names or rules differ from their declaration.</param>
/// <param name="UnchangedCount">How many declared types are already stored as declared.</param>
public sealed record AttachmentTypeSeedPlan(IReadOnlyList<ServiceAttachment> ToAdd, IReadOnlyList<AttachmentTypeRefresh> ToRefresh, int UnchangedCount)
{
    /// <summary>True when the run writes nothing.</summary>
    public bool IsEmpty => ToAdd.Count == 0 && ToRefresh.Count == 0;

    /// <summary>Applies every refresh to its stored type (the caller saves); returns how many changed.</summary>
    public int ApplyRefreshes() => ToRefresh.Count(refresh => refresh.Stored.Refresh(refresh.Declared));
}

/// <summary>A stored type and the declaration it is refreshed to.</summary>
/// <param name="Stored">The tracked stored type.</param>
/// <param name="Declared">Its declaration.</param>
public sealed record AttachmentTypeRefresh(ServiceAttachment Stored, ServiceAttachmentSeed Declared);

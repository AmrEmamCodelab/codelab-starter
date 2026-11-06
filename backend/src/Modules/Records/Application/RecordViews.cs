using Starter.Application.Records;
using Starter.Application.Users;
using Starter.Application.Workflow;
using Starter.Domain.Workflow;
using Starter.Modules.Records.Domain;

namespace Starter.Modules.Records.Application;

/// <summary>One record in a list (my records, oversight).</summary>
/// <param name="Record">The record.</param>
/// <param name="StatusKey">The kebab key of its status.</param>
/// <param name="IsTerminal">True when its status is terminal.</param>
/// <param name="OwnerDisplayName">The owner's display name (oversight), or null (my records).</param>
public sealed record RecordListItemView(Record Record, string StatusKey, bool IsTerminal, string? OwnerDisplayName);

/// <summary>A page of a record list.</summary>
/// <param name="Items">The items, newest first.</param>
/// <param name="Page">The 1-based page.</param>
/// <param name="PageSize">The page size.</param>
/// <param name="Total">Every match.</param>
public sealed record RecordListPage(IReadOnlyList<RecordListItemView> Items, int Page, int PageSize, int Total);

/// <summary>One record as the caller's audience sees it.</summary>
/// <param name="Record">The record.</param>
/// <param name="Type">Its type.</param>
/// <param name="Audience">How the caller sees it (selects nothing here; the wire carries every wording key).</param>
/// <param name="OwnerDisplayName">The owner's display name.</param>
/// <param name="LastReason">The reason of the latest timeline entry, or null.</param>
/// <param name="AllowedActions">What the caller may do now (empty for a terminal record and for oversight).</param>
/// <param name="Enrichment">The current assignee and the SLA badge, when those modules are present.</param>
public sealed record RecordDetailView(
    Record Record,
    RegisteredRecordType Type,
    WorkflowAudience Audience,
    string OwnerDisplayName,
    string? LastReason,
    IReadOnlyList<AllowedAction> AllowedActions,
    RecordEnrichmentView Enrichment)
{
    /// <summary>The status definition (null only when the catalog lost the status: then the record reads as its number).</summary>
    public WorkflowStatusDefinition? Status => Type.Catalog.FindStatus(Record.Status);
}

/// <summary>The optional facts other modules add to a record (null when the module is absent or has nothing).</summary>
/// <param name="Assignee">The current assignee, or null.</param>
/// <param name="Sla">The SLA badge, or null.</param>
public sealed record RecordEnrichmentView(AssignmentSnapshot? Assignee, SlaBadge? Sla);

/// <summary>One timeline entry.</summary>
/// <param name="Entry">The history row.</param>
/// <param name="ToStatusKey">The kebab key of the status it moved to.</param>
public sealed record TimelineEntryView(RecordHistory Entry, string ToStatusKey);

/// <summary>
/// The assignee and SLA readers (Assignments, Deadlines), optional: with none registered every record reads as
/// unassigned and without a badge.
/// </summary>
public sealed class RecordEnrichment(IEnumerable<IRecordAssignmentReader> assignments, IEnumerable<IRecordSlaReader> deadlines)
{
    /// <summary>The enrichment of record <paramref name="recordId"/>.</summary>
    public async Task<RecordEnrichmentView> ForAsync(long recordId, CancellationToken cancellationToken)
    {
        AssignmentSnapshot? assignee = null;
        foreach (var reader in assignments)
        {
            assignee ??= (await reader.CurrentAsync([recordId], cancellationToken)).GetValueOrDefault(recordId);
        }

        SlaBadge? sla = null;
        foreach (var reader in deadlines)
        {
            sla ??= (await reader.BadgesAsync([recordId], cancellationToken)).GetValueOrDefault(recordId);
        }

        return new RecordEnrichmentView(assignee, sla);
    }
}

/// <summary>Display names of users by internal id, through the kernel <see cref="IAccountAdministration"/> port (Users).</summary>
public sealed class RecordUserNames(IAccountAdministration accounts)
{
    /// <summary>The name shown for an account that no longer exists.</summary>
    public const string Unknown = "-";

    /// <summary>The display name of every distinct id in <paramref name="userIds"/> (<see cref="Unknown"/> for a missing account).</summary>
    public async Task<IReadOnlyDictionary<long, string>> ForAsync(IEnumerable<long> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var names = new Dictionary<long, string>();
        foreach (var userId in userIds.Distinct())
        {
            var account = await accounts.FindAsync(userId, cancellationToken);
            names[userId] = account?.DisplayName ?? Unknown;
        }

        return names;
    }
}

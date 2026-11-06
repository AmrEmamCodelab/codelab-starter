using Starter.Application.Records;

namespace Starter.Modules.Deadlines.Application;

/// <summary>
/// Who receives the escalation of a breached record: its current assignee (<see cref="IRecordAssignmentReader"/>,
/// when one is registered), otherwise the staff who hold the record type's queue permission at a scope that reaches the
/// record's organization (<see cref="RecordTypeRegistration.QueuePermissionKey"/>, <see cref="IPermissionHolderDirectory"/>).
/// Both ports are optional; without them a record has no recipient.
/// </summary>
public sealed class EscalationRecipients(
    IEnumerable<IRecordAssignmentReader> assignments,
    IEnumerable<IPermissionHolderDirectory> staff,
    IEnumerable<IRecordTypeRegistrationSource> registrations)
{
    /// <summary>The recipients of each of <paramref name="records"/> (distinct user ids, ascending; empty when nobody can be named).</summary>
    public async Task<IReadOnlyDictionary<long, IReadOnlyList<long>>> ResolveAsync(
        IReadOnlyCollection<RecordSummary> records,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        var assignees = await AssigneesAsync([.. records.Select(record => record.Id)], cancellationToken);
        var queueStaff = new Dictionary<(string RecordType, long? OrganizationId), IReadOnlyList<long>>();
        var recipients = new Dictionary<long, IReadOnlyList<long>>();
        foreach (var record in records)
        {
            if (assignees.TryGetValue(record.Id, out var assignee))
            {
                recipients[record.Id] = [assignee.AssigneeUserId];
                continue;
            }

            var key = (record.RecordType, record.OrganizationId);
            if (!queueStaff.TryGetValue(key, out var holders))
            {
                holders = await QueueStaffAsync(record, cancellationToken);
                queueStaff[key] = holders;
            }

            recipients[record.Id] = holders;
        }

        return recipients;
    }

    private async Task<Dictionary<long, AssignmentSnapshot>> AssigneesAsync(IReadOnlyCollection<long> recordIds, CancellationToken cancellationToken)
    {
        var current = new Dictionary<long, AssignmentSnapshot>();
        if (recordIds.Count == 0)
        {
            return current;
        }

        foreach (var reader in assignments)
        {
            foreach (var (recordId, snapshot) in await reader.CurrentAsync(recordIds, cancellationToken))
            {
                current.TryAdd(recordId, snapshot);
            }
        }

        return current;
    }

    private async Task<IReadOnlyList<long>> QueueStaffAsync(RecordSummary record, CancellationToken cancellationToken)
    {
        var registration = registrations
            .SelectMany(source => source.Registrations)
            .FirstOrDefault(candidate => string.Equals(candidate.RecordType, record.RecordType, StringComparison.Ordinal));
        if (registration is null)
        {
            return [];
        }

        var holders = new HashSet<long>();
        foreach (var directory in staff)
        {
            holders.UnionWith(await directory.HoldersAsync(registration.QueuePermissionKey, record.OrganizationId, cancellationToken));
        }

        return [.. holders.Order()];
    }
}

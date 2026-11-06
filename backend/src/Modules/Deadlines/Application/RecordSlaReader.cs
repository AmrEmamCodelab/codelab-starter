using Starter.Application.Records;

namespace Starter.Modules.Deadlines.Application;

/// <summary>
/// The SLA badges of the queue and detail views: the badge of each record's open deadline at the current
/// <see cref="TimeProvider"/> time (<see cref="Domain.RecordDeadline.BadgeAt"/>). Records without an open deadline
/// (no SLA in their status, or terminal) are absent.
/// </summary>
public sealed class RecordSlaReader(IRecordDeadlineStore deadlines, TimeProvider time) : IRecordSlaReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, SlaBadge>> BadgesAsync(IReadOnlyCollection<long> recordIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordIds);
        var badges = new Dictionary<long, SlaBadge>();
        if (recordIds.Count == 0)
        {
            return badges;
        }

        var open = await deadlines.OpenForAsync(recordIds, cancellationToken);
        var now = time.GetUtcNow();
        foreach (var deadline in open)
        {
            badges.TryAdd(deadline.RecordId, deadline.BadgeAt(now));
        }

        return badges;
    }
}

using Microsoft.EntityFrameworkCore;
using Starter.Modules.Deadlines.Application;
using Starter.Modules.Deadlines.Domain;

namespace Starter.Modules.Deadlines.Infrastructure.Persistence;

/// <summary>The EF Core <see cref="IRecordDeadlineStore"/> over <see cref="DeadlinesDbContext"/>.</summary>
public sealed class RecordDeadlineStore(DeadlinesDbContext context) : IRecordDeadlineStore
{
    /// <inheritdoc />
    public Task<RecordDeadline?> FindOpenAsync(long recordId, string recordType, CancellationToken cancellationToken) =>
        context.RecordDeadlines
            .Where(deadline => deadline.RecordId == recordId && deadline.RecordType == recordType && deadline.ClosedAt == null)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordDeadline>> OpenForAsync(IReadOnlyCollection<long> recordIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordIds);
        return await context.RecordDeadlines
            .AsNoTracking()
            .Where(deadline => recordIds.Contains(deadline.RecordId) && deadline.ClosedAt == null)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordDeadline>> DueForBreachAsync(DateTimeOffset now, int take, CancellationToken cancellationToken) =>
        await context.RecordDeadlines
            .Where(deadline => deadline.ClosedAt == null && deadline.BreachedAt == null && deadline.DueAt <= now)
            .OrderBy(deadline => deadline.DueAt)
            .ThenBy(deadline => deadline.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordDeadline>> AwaitingEscalationAsync(int take, CancellationToken cancellationToken) =>
        await context.RecordDeadlines
            .Where(deadline => deadline.ClosedAt == null && deadline.BreachedAt != null && deadline.EscalatedAt == null)
            .OrderBy(deadline => deadline.DueAt)
            .ThenBy(deadline => deadline.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(RecordDeadline deadline) => context.RecordDeadlines.Add(deadline);

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}

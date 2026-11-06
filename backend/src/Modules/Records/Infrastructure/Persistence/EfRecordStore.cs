using Microsoft.EntityFrameworkCore;
using Starter.Application.Records;
using Starter.Modules.Records.Application;
using Starter.Modules.Records.Domain;

namespace Starter.Modules.Records.Infrastructure.Persistence;

/// <summary>The <see cref="IRecordStore"/> over <see cref="RecordsDbContext"/>.</summary>
public sealed class EfRecordStore(RecordsDbContext context) : IRecordStore
{
    /// <inheritdoc />
    public Task<Record?> FindAsync(long recordId, CancellationToken cancellationToken) =>
        context.Records.SingleOrDefaultAsync(record => record.Id == recordId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Record>> ListAsync(IReadOnlyCollection<long> recordIds, CancellationToken cancellationToken) =>
        await context.Records.AsNoTracking().Where(record => recordIds.Contains(record.Id)).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<PagedResult<Record>> PageAsync(RecordPageQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var filtered = RecordFilters.Apply(context.Records.AsNoTracking(), query);
        var total = await filtered.CountAsync(cancellationToken);
        var filter = query.Filter;
        var items = await filtered
            .OrderByDescending(record => record.CreatedAt)
            .ThenByDescending(record => record.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Record>(items, filter.Page, filter.PageSize, total);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordHistory>> HistoryAsync(long recordId, CancellationToken cancellationToken) =>
        await context.History.AsNoTracking()
            .Where(entry => entry.RecordId == recordId)
            .OrderBy(entry => entry.At)
            .ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<RecordHistory?> LatestHistoryAsync(long recordId, CancellationToken cancellationToken) =>
        context.History.AsNoTracking()
            .Where(entry => entry.RecordId == recordId)
            .OrderByDescending(entry => entry.At)
            .ThenByDescending(entry => entry.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<long> AddDraftAsync(Record record, Func<long, RecordHistory> firstEntry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(firstEntry);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        context.Records.Add(record);
        await context.SaveChangesAsync(cancellationToken);
        context.History.Add(firstEntry(record.Id));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return record.Id;
    }

    /// <inheritdoc />
    public void Add(RecordHistory entry) => context.History.Add(entry);

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return false;
        }
    }
}

/// <summary>The filters of a record page query, as one composable query (the store's only filtering rule).</summary>
public static class RecordFilters
{
    /// <summary><paramref name="records"/> narrowed to <paramref name="query"/>.</summary>
    public static IQueryable<Record> Apply(IQueryable<Record> records, RecordPageQuery query)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(query);
        var filter = query.Filter;
        var result = records.Where(record => record.RecordType == filter.RecordType);
        result = query.OwnerUserId is { } owner ? result.Where(record => record.OwnerUserId == owner) : result;
        result = query.SubmittedOnly ? result.Where(record => record.SubmittedAt != null) : result;
        result = filter.Statuses is { Count: > 0 } statuses ? result.Where(record => statuses.Contains(record.Status)) : result;
        result = filter.NumberContains is { } number ? result.Where(record => record.Number != null && record.Number.Contains(number)) : result;
        result = filter.OrganizationId is { } organization ? result.Where(record => record.OrganizationId == organization) : result;
        result = filter.SubmittedFrom is { } from ? result.Where(record => record.SubmittedAt >= from) : result;
        result = filter.SubmittedTo is { } to ? result.Where(record => record.SubmittedAt <= to) : result;
        return filter.Ids is { } ids ? result.Where(record => ids.Contains(record.Id)) : result;
    }
}

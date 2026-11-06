using Starter.Application.Persistence;
using Starter.Application.Records;
using Starter.Modules.Records.Domain;

namespace Starter.Modules.Records.Application;

/// <summary>The Records tables (<c>records.Records</c>, <c>records.RecordHistory</c>), implemented in Infrastructure.</summary>
public interface IRecordStore : IUnitOfWork
{
    /// <summary>The record (tracked, so a change is saved), or null.</summary>
    Task<Record?> FindAsync(long recordId, CancellationToken cancellationToken);

    /// <summary>The records with these ids that exist (read only), in no particular order.</summary>
    Task<IReadOnlyList<Record>> ListAsync(IReadOnlyCollection<long> recordIds, CancellationToken cancellationToken);

    /// <summary>One page of records matching <paramref name="query"/>, newest first (read only).</summary>
    Task<PagedResult<Record>> PageAsync(RecordPageQuery query, CancellationToken cancellationToken);

    /// <summary>The record's timeline, oldest first.</summary>
    Task<IReadOnlyList<RecordHistory>> HistoryAsync(long recordId, CancellationToken cancellationToken);

    /// <summary>The record's latest timeline entry, or null when it has none.</summary>
    Task<RecordHistory?> LatestHistoryAsync(long recordId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts a new draft and its first timeline entry in ONE transaction (the entry needs the record's id, so the
    /// store writes the record first); returns the new record id.
    /// </summary>
    /// <param name="record">The new draft.</param>
    /// <param name="firstEntry">Builds the first entry from the new record id.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    Task<long> AddDraftAsync(Record record, Func<long, RecordHistory> firstEntry, CancellationToken cancellationToken);

    /// <summary>Stages a new timeline entry.</summary>
    void Add(RecordHistory entry);

    /// <summary>
    /// Writes the staged changes in one transaction; false (nothing written, the staged changes dropped) when another
    /// request changed the record since it was read (rowversion).
    /// </summary>
    Task<bool> TrySaveAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The filter of <see cref="IRecordStore.PageAsync"/>. Every filter but the record type is optional (null matches all).
/// </summary>
/// <param name="Filter">The kernel query (record type, statuses, number part, organization, submit window, ids, paging).</param>
/// <param name="OwnerUserId">Only this owner's records, or null.</param>
/// <param name="SubmittedOnly">True to leave out records never submitted (drafts are private to their owner).</param>
public sealed record RecordPageQuery(RecordQuery Filter, long? OwnerUserId, bool SubmittedOnly);

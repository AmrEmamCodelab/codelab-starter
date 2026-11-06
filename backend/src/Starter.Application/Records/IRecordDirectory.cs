namespace Starter.Application.Records;

/// <summary>
/// Read access to the record-type-neutral record facts, implemented by the Records module. Queues, deadlines and
/// oversight read records through it, never through the Records tables. It applies no permission or ownership filter:
/// the caller owns authorization and narrows the query (owner, organization, ids).
/// </summary>
public interface IRecordDirectory
{
    /// <summary>The summaries of the records with <paramref name="ids"/> that exist, in no particular order (missing ids are skipped).</summary>
    Task<IReadOnlyList<RecordSummary>> GetAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken);

    /// <summary>One page of the records matching <paramref name="query"/>, newest submit first (drafts last, newest first).</summary>
    Task<PagedResult<RecordSummary>> QueryAsync(RecordQuery query, CancellationToken cancellationToken);
}

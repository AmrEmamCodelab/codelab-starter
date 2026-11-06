using Starter.Domain.Records;

namespace Starter.Application.Records;

/// <summary>
/// A filter over the records of one record type (<see cref="IRecordDirectory.QueryAsync"/>). Every null filter matches
/// everything; the filters combine with AND. Page and size come normalized (<see cref="RecordPageRequest.From"/>).
/// </summary>
/// <param name="RecordType">The record type key.</param>
/// <param name="Statuses">Only these status values, or null for all.</param>
/// <param name="NumberContains">Only numbers containing this text (ordinal, case-insensitive), or null.</param>
/// <param name="OrganizationId">Only records of this organization, or null (the caller's organization scope decides).</param>
/// <param name="SubmittedFrom">Only records first submitted at or after this instant, or null.</param>
/// <param name="SubmittedTo">Only records first submitted before this instant, or null.</param>
/// <param name="Ids">Only these record ids (for example the caller's own or the ones assigned to them), or null.</param>
/// <param name="Page">The 1-based page.</param>
/// <param name="PageSize">The page size, 1 to <see cref="RecordsLimits.MaxPageSize"/>.</param>
/// <exception cref="ArgumentException">The record type key is malformed.</exception>
/// <exception cref="ArgumentOutOfRangeException">The page or the page size is out of range.</exception>
public sealed record RecordQuery(
    string RecordType,
    IReadOnlyCollection<int>? Statuses,
    string? NumberContains,
    long? OrganizationId,
    DateTimeOffset? SubmittedFrom,
    DateTimeOffset? SubmittedTo,
    IReadOnlyCollection<long>? Ids,
    int Page,
    int PageSize)
{
    /// <summary>The record type key.</summary>
    public string RecordType { get; } = RecordTypeKeyFormat.EnsureValid(RecordType, nameof(RecordType));

    /// <summary>The 1-based page.</summary>
    public int Page { get; } = RecordPageRequest.EnsurePage(Page, nameof(Page));

    /// <summary>The page size.</summary>
    public int PageSize { get; } = RecordPageRequest.EnsurePageSize(PageSize, nameof(PageSize));
}

/// <summary>A normalized page request of the record, queue and oversight lists.</summary>
/// <param name="Page">The 1-based page.</param>
/// <param name="PageSize">The page size, 1 to <see cref="RecordsLimits.MaxPageSize"/>.</param>
public sealed record RecordPageRequest(int Page, int PageSize)
{
    /// <summary>
    /// The page request of raw query values: a missing or non-positive page is 1, a missing or non-positive size is
    /// <see cref="RecordsLimits.DefaultPageSize"/>, a larger size is capped at <see cref="RecordsLimits.MaxPageSize"/>.
    /// </summary>
    public static RecordPageRequest From(int? page, int? pageSize) =>
        new(
            Page: page is > 0 ? page.Value : 1,
            PageSize: pageSize is > 0 ? Math.Min(pageSize.Value, RecordsLimits.MaxPageSize) : RecordsLimits.DefaultPageSize);

    /// <summary>Refuses a page below 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The page is below 1.</exception>
    public static int EnsurePage(int page, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1, parameterName);
        return page;
    }

    /// <summary>Refuses a page size outside 1 to <see cref="RecordsLimits.MaxPageSize"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The size is out of range.</exception>
    public static int EnsurePageSize(int pageSize, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, RecordsLimits.MaxPageSize, parameterName);
        return pageSize;
    }
}

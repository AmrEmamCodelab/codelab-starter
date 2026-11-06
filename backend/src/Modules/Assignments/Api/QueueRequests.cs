using Starter.Modules.Assignments.Application.Queues;

namespace Starter.Modules.Assignments.Api;

/// <summary>
/// The query string of <c>GET api/queues/{recordType}</c>, bound by name (case-insensitive):
/// <c>view</c>, <c>status</c>, <c>number</c>, <c>submittedFrom</c>, <c>submittedTo</c>, <c>page</c>, <c>pageSize</c>.
/// </summary>
/// <param name="View">The view word: <c>unassigned</c>, <c>mine</c> or <c>all</c> (required).</param>
/// <param name="Status">One open status value, or null for all of them.</param>
/// <param name="Number">Part of the record number, or null.</param>
/// <param name="SubmittedFrom">The earliest first-submit instant (ISO 8601), or null.</param>
/// <param name="SubmittedTo">The first-submit instant the range ends before (ISO 8601), or null.</param>
/// <param name="Page">The 1-based page, or null.</param>
/// <param name="PageSize">The page size, or null.</param>
public sealed record WorkQueueQuery(
    string? View,
    int? Status,
    string? Number,
    DateTimeOffset? SubmittedFrom,
    DateTimeOffset? SubmittedTo,
    int? Page,
    int? PageSize)
{
    /// <summary>The use-case request for the queue of <paramref name="recordType"/>.</summary>
    public WorkQueueRequest ToRequest(string recordType) =>
        new(
            RecordType: recordType,
            View: View,
            Status: Status,
            Number: Number,
            SubmittedFrom: SubmittedFrom,
            SubmittedTo: SubmittedTo,
            Page: Page,
            PageSize: PageSize);
}

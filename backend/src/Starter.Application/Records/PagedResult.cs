namespace Starter.Application.Records;

/// <summary>One page of items and the total count across all pages.</summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The items of this page (at most <paramref name="PageSize"/>).</param>
/// <param name="Page">The 1-based page.</param>
/// <param name="PageSize">The requested page size.</param>
/// <param name="Total">The number of matching items across all pages.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

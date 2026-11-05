using Starter.Modules.Users.Domain;

namespace Starter.Modules.Users.Application;

/// <summary>A normalized page request: page 1 or later, size between 1 and <see cref="UsersLimits.MaxPageSize"/>.</summary>
/// <param name="Page">The 1-based page.</param>
/// <param name="PageSize">The page size.</param>
public sealed record PageRequest(int Page, int PageSize)
{
    /// <summary>
    /// The page request of the raw query values: a missing or non-positive page is 1, a missing or non-positive size is
    /// <see cref="UsersLimits.DefaultPageSize"/>, a larger size is capped at <see cref="UsersLimits.MaxPageSize"/>.
    /// </summary>
    public static PageRequest From(int? page, int? pageSize) =>
        new(
            Page: page is > 0 ? page.Value : 1,
            PageSize: pageSize is > 0 ? Math.Min(pageSize.Value, UsersLimits.MaxPageSize) : UsersLimits.DefaultPageSize);

    /// <summary>The trimmed search text (cut to <see cref="UsersLimits.SearchMaxLength"/>), or null when blank.</summary>
    public static string? Search(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var trimmed = search.Trim();
        return trimmed.Length > UsersLimits.SearchMaxLength ? trimmed[..UsersLimits.SearchMaxLength] : trimmed;
    }
}

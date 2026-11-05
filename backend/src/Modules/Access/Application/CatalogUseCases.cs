using Starter.Application.Authorization;
using Starter.Application.Results;
using Starter.Application.Users;

namespace Starter.Modules.Access.Application;

/// <summary>One permission of the catalog as the editors see it.</summary>
/// <param name="Key">The permission key; the editors build their tree from its dotted segments.</param>
/// <param name="Module">The owner segment of the key (the declaring module, kebab case).</param>
/// <param name="Description">What holding it allows, in English (the screens translate <c>permissions.&lt;key&gt;</c>).</param>
/// <param name="IsRetired">True when the key left the code: it grants nothing and cannot be granted again.</param>
public sealed record PermissionInfo(string Key, string Module, string Description, bool IsRetired);

/// <summary>Lists the permission catalog, retired keys included. The permission is enforced by the endpoint.</summary>
public sealed class ListPermissions(IPermissionRepository permissions)
{
    /// <summary>Every catalog row, sorted by key (ordinal).</summary>
    public async Task<Outcome<IReadOnlyList<PermissionInfo>>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var rows = await permissions.ListAllAsync(cancellationToken);
        IReadOnlyList<PermissionInfo> catalog =
        [
            .. rows.OrderBy(row => row.Key, StringComparer.Ordinal)
                .Select(row => new PermissionInfo(Key: row.Key, Module: row.Owner, Description: row.Description, IsRetired: row.IsObsolete)),
        ];
        return Outcome.Success(catalog);
    }
}

/// <summary>
/// The user picker of the grant editor: one page of accounts matching the search, through the kernel account port
/// (Access never reads the Identity or Users tables). The permission is enforced by the endpoint; a holder of
/// <see cref="AccessPermissionKeys.GrantsView"/> at own-organization scope finds only their organization's accounts.
/// </summary>
public sealed class SearchAccessUsers(IAccountAdministration accounts, IOrganizationScope scope)
{
    /// <summary>The most accounts one search returns.</summary>
    public const int PageSize = 20;

    /// <summary>The longest search text used (longer text is cut, never refused).</summary>
    public const int SearchMaxLength = 128;

    private const int FirstPage = 1;

    /// <summary>Up to <see cref="PageSize"/> accounts, active and deactivated, in the port's stable order.</summary>
    public async Task<Outcome<IReadOnlyList<AccountSummary>>> ExecuteAsync(string? search, CancellationToken cancellationToken)
    {
        var text = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (text is { Length: > SearchMaxLength })
        {
            text = text[..SearchMaxLength];
        }

        var filter = (await scope.ScopeForAsync(AccessPermissionKeys.GrantsView, cancellationToken)).Narrow(requestedOrganizationId: null);
        if (filter.MatchesNothing)
        {
            return Outcome.Success<IReadOnlyList<AccountSummary>>([]);
        }

        var page = await accounts.ListAsync(
            new AccountQuery(Search: text, OrganizationId: filter.OrganizationId, Kind: null, IsActive: null, Page: FirstPage, PageSize: PageSize),
            cancellationToken);
        return Outcome.Success(page.Items);
    }
}

using Starter.Application.Localization;
using Starter.Application.Users;
using Starter.Domain.Localization;

namespace Starter.Modules.Users.Application.Accounts;

/// <summary>An account with its organization's name in the caller's language.</summary>
/// <param name="Account">The account.</param>
/// <param name="OrganizationName">The organization's name, or null when the account has none.</param>
public sealed record UserView(AccountSummary Account, string? OrganizationName);

/// <summary>One page of accounts with their organization names.</summary>
/// <param name="Items">The accounts of the page.</param>
/// <param name="Total">The number of matches on every page.</param>
public sealed record UserViewPage(IReadOnlyList<UserView> Items, int Total);

/// <summary>Joins accounts (from Identity) with this module's organizations, one query per page.</summary>
public sealed class UserViews(IOrganizationRepository organizations, ICurrentLanguage language)
{
    /// <summary>The views of <paramref name="accounts"/>, in the same order.</summary>
    public async Task<IReadOnlyList<UserView>> OfAsync(IReadOnlyList<AccountSummary> accounts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        var ids = accounts.Select(account => account.OrganizationId).OfType<long>().Distinct().ToArray();
        var byId = ids.Length == 0 ? new Dictionary<long, Domain.Organization>() : await organizations.FindManyAsync(ids, cancellationToken);
        var arabic = language.Language == AppLanguage.Arabic;
        return [.. accounts.Select(account => new UserView(account, NameOf(account.OrganizationId, byId, arabic)))];
    }

    /// <summary>The view of one account.</summary>
    public async Task<UserView> OfAsync(AccountSummary account, CancellationToken cancellationToken) =>
        (await OfAsync([account], cancellationToken))[0];

    private static string? NameOf(long? organizationId, IReadOnlyDictionary<long, Domain.Organization> byId, bool arabic) =>
        organizationId is { } id && byId.TryGetValue(id, out var organization) ? organization.NameIn(arabic) : null;
}

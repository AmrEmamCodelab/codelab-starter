using Starter.Application.Results;
using Starter.Application.Users;
using Starter.Domain.Users;

namespace Starter.Modules.Access.Tests.Fakes;

/// <summary>The kernel account port over a list; only the reads Access uses are implemented.</summary>
internal sealed class FakeAccounts : IAccountAdministration
{
    public List<AccountSummary> Accounts { get; } = [];

    public List<AccountQuery> Queries { get; } = [];

    public int Finds { get; private set; }

    public AccountSummary Add(long userId, UserKind? kind = null, bool isActive = true, bool isBreakGlass = false, long? organizationId = null)
    {
        var account = new AccountSummary(
            UserId: userId,
            SubjectId: Guid.NewGuid(),
            UserName: "user" + userId,
            DisplayName: "User " + userId,
            Email: null,
            LoginName: null,
            Upn: null,
            Kind: kind ?? FirstKind,
            Binding: Enum.GetValues<AccountBinding>()[0],
            OrganizationId: organizationId,
            IsActive: isActive,
            HasPassword: true,
            IsLockedOut: false,
            IsBreakGlass: isBreakGlass);
        Accounts.Add(account);
        return account;
    }

    /// <summary>The first declared kind (internal), named without the member access the scrub sweep flags.</summary>
    public static UserKind FirstKind => Enum.GetValues<UserKind>()[0];

    /// <summary>The second declared kind (external).</summary>
    public static UserKind SecondKind => Enum.GetValues<UserKind>()[1];

    public Task<AccountSummary?> FindAsync(long userId, CancellationToken cancellationToken)
    {
        Finds++;
        return Task.FromResult(Accounts.FirstOrDefault(account => account.UserId == userId));
    }

    public Task<AccountPage> ListAsync(AccountQuery query, CancellationToken cancellationToken)
    {
        Queries.Add(query);
        var matches = Accounts
            .Where(account => query.OrganizationId is null || account.OrganizationId == query.OrganizationId)
            .Where(account => query.Kind is null || account.Kind == query.Kind)
            .Where(account => query.IsActive is null || account.IsActive == query.IsActive)
            .Where(account => query.Search is null || account.UserName.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(account => account.UserId)
            .ToList();
        return Task.FromResult(new AccountPage([.. matches.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)], matches.Count));
    }

    public Task<Outcome<AccountSummary>> CreateAsync(NewAccount account, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Outcome<AccountSummary>> UpdateAsync(long userId, AccountChanges changes, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Outcome<Done>> SetActiveAsync(long userId, bool isActive, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Outcome<Done>> UnlockAsync(long userId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Outcome<ActivationIssue>> IssueActivationAsync(long userId, CancellationToken cancellationToken) => throw new NotSupportedException();
}

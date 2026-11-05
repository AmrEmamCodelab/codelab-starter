using System.Reflection;
using Starter.Application.Authorization;
using Starter.Application.Localization;
using Starter.Application.Messaging;
using Starter.Application.Results;
using Starter.Application.Users;
using Starter.Domain.Localization;
using Starter.Domain.Users;
using Starter.Modules.Users.Application;
using Starter.Modules.Users.Domain;

namespace Starter.Modules.Users.Tests.Fakes;

/// <summary>An in-memory Identity: accounts, duplicates refused by user name, activation tickets by counter.</summary>
internal sealed class FakeAccounts : IAccountAdministration
{
    public const string DuplicateCode = "IDENTITY_USER_NAME_TAKEN";
    public const string NotPasswordCode = "IDENTITY_ACTIVATION_NOT_ALLOWED";
    public static readonly DateTimeOffset TicketExpiry = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    private long _nextId = 100;
    private int _tickets;

    public List<AccountSummary> Accounts { get; } = [];

    public List<string> Calls { get; } = [];

    public List<AccountQuery> Queries { get; } = [];

    public Refusal? IssueRefusal { get; set; }

    public Func<NewAccount, Refusal?> RefuseCreate { get; set; } = _ => null;

    public AccountSummary Add(string userName, AccountBinding binding = AccountBinding.Password, UserKind kind = UserKind.External, long? organizationId = null, bool isBreakGlass = false)
    {
        var account = new AccountSummary(
            UserId: ++_nextId, SubjectId: Guid.NewGuid(), UserName: userName, DisplayName: userName + " name", Email: null, LoginName: null, Upn: null,
            Kind: kind, Binding: binding, OrganizationId: organizationId, IsActive: true, HasPassword: false, IsLockedOut: false, IsBreakGlass: isBreakGlass);
        Accounts.Add(account);
        return account;
    }

    public Task<Outcome<AccountSummary>> CreateAsync(NewAccount account, CancellationToken cancellationToken)
    {
        Calls.Add($"create:{account.UserName}");
        if (RefuseCreate(account) is { } refusal)
        {
            return Task.FromResult<Outcome<AccountSummary>>(refusal);
        }

        if (Accounts.Any(existing => string.Equals(existing.UserName, account.UserName, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult<Outcome<AccountSummary>>(Refusal.Conflict(DuplicateCode));
        }

        var created = new AccountSummary(
            ++_nextId, Guid.NewGuid(), account.UserName, account.DisplayName, account.Email, account.LoginName, account.Upn,
            account.Kind, account.Binding, account.OrganizationId, IsActive: true, HasPassword: false, IsLockedOut: false, IsBreakGlass: false);
        Accounts.Add(created);
        return Task.FromResult<Outcome<AccountSummary>>(created);
    }

    public Task<Outcome<AccountSummary>> UpdateAsync(long userId, AccountChanges changes, CancellationToken cancellationToken)
    {
        Calls.Add($"update:{userId}");
        var index = Accounts.FindIndex(account => account.UserId == userId);
        var updated = Accounts[index] with
        {
            DisplayName = changes.DisplayName, Email = changes.Email, LoginName = changes.LoginName, Upn = changes.Upn,
            Kind = changes.Kind, Binding = changes.Binding, OrganizationId = changes.OrganizationId,
        };
        Accounts[index] = updated;
        return Task.FromResult<Outcome<AccountSummary>>(updated);
    }

    public Task<Outcome<Done>> SetActiveAsync(long userId, bool isActive, CancellationToken cancellationToken)
    {
        Calls.Add($"active:{userId}:{isActive}");
        var index = Accounts.FindIndex(account => account.UserId == userId);
        Accounts[index] = Accounts[index] with { IsActive = isActive };
        return Task.FromResult(Outcome.Success(Done.Value));
    }

    public Task<Outcome<Done>> UnlockAsync(long userId, CancellationToken cancellationToken)
    {
        Calls.Add($"unlock:{userId}");
        return Task.FromResult(Outcome.Success(Done.Value));
    }

    public Task<AccountSummary?> FindAsync(long userId, CancellationToken cancellationToken) =>
        Task.FromResult(Accounts.FirstOrDefault(account => account.UserId == userId));

    public Task<AccountPage> ListAsync(AccountQuery query, CancellationToken cancellationToken)
    {
        Queries.Add(query);
        var matches = Accounts.Where(account => Matches(account, query)).ToList();
        var page = matches.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
        return Task.FromResult(new AccountPage(page, matches.Count));
    }

    public Task<Outcome<ActivationIssue>> IssueActivationAsync(long userId, CancellationToken cancellationToken)
    {
        Calls.Add($"activation:{userId}");
        if (IssueRefusal is { } refusal)
        {
            return Task.FromResult<Outcome<ActivationIssue>>(refusal);
        }

        var account = Accounts.First(candidate => candidate.UserId == userId);
        if (account.Binding != AccountBinding.Password)
        {
            return Task.FromResult<Outcome<ActivationIssue>>(Refusal.Unprocessable(NotPasswordCode));
        }

        _tickets++;
        return Task.FromResult<Outcome<ActivationIssue>>(new ActivationIssue($"sealed-ticket-{_tickets}", $"ticket-id-{_tickets}", TicketExpiry));
    }

    private static bool Matches(AccountSummary account, AccountQuery query) =>
        (query.Search is null || account.UserName.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
        && (query.OrganizationId is null || account.OrganizationId == query.OrganizationId)
        && (query.Kind is null || account.Kind == query.Kind)
        && (query.IsActive is null || account.IsActive == query.IsActive);
}

internal sealed class FakeOutbox : IOutbox
{
    public List<OutboxMessage> Messages { get; } = [];

    public Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class FakeOrganizations : IOrganizationRepository
{
    private long _nextId;

    public List<Organization> Items { get; } = [];

    public Organization Add(string code, OrganizationKind kind, bool isActive = true)
    {
        var organization = Organization.Create(new OrganizationDetails(code, code + " en", code + " ar", kind), TestClock.Start);
        organization.SetActive(isActive, TestClock.Start);
        Add(organization);
        return organization;
    }

    public void Add(Organization organization)
    {
        Ids.Assign(organization, ++_nextId);
        Items.Add(organization);
    }

    public Task<Organization?> FindAsync(long id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(organization => organization.Id == id));

    public Task<Organization?> FindByCodeAsync(string normalizedCode, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(organization => organization.Code == normalizedCode));

    public Task<IReadOnlyDictionary<string, Organization>> FindByCodesAsync(IReadOnlyCollection<string> normalizedCodes, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, Organization>>(
            Items.Where(organization => normalizedCodes.Contains(organization.Code)).ToDictionary(organization => organization.Code, StringComparer.Ordinal));

    public Task<IReadOnlyDictionary<long, Organization>> FindManyAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<long, Organization>>(
            Items.Where(organization => ids.Contains(organization.Id)).ToDictionary(organization => organization.Id));

    public Task<OrganizationPage> ListAsync(OrganizationQuery query, CancellationToken cancellationToken)
    {
        var matches = Items.Where(organization => (query.Kind is null || organization.Kind == query.Kind) && (query.IsActive is null || organization.IsActive == query.IsActive)
            && (query.Id is null || organization.Id == query.Id)).ToList();
        return Task.FromResult(new OrganizationPage([.. matches.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)], matches.Count));
    }
}

internal sealed class FakeBatches : IImportBatchRepository
{
    private long _nextId;

    public List<ImportBatch> Items { get; } = [];

    public void Add(ImportBatch batch)
    {
        Ids.Assign(batch, ++_nextId);
        Items.Add(batch);
    }

    public Task<ImportBatch?> FindAsync(long id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(batch => batch.Id == id));
}

internal sealed class FakeUnitOfWork : IUsersUnitOfWork
{
    public int Saves { get; private set; }

    public bool NextTrySaveSucceeds { get; set; } = true;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.FromResult(1);
    }

    public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.FromResult(NextTrySaveSucceeds);
    }
}

internal sealed class FakePublicIds : IUserPublicIds
{
    public string TokenOf(long userId) => $"m1_users_{userId}";
}

internal sealed class FakeCurrentUser : ICurrentUserContext
{
    public bool IsAuthenticated => SubjectId is not null;

    public Guid? SubjectId { get; set; } = Guid.NewGuid();

    public string? SessionId => IsAuthenticated ? "session" : null;
}

internal sealed class FakeLanguage : ICurrentLanguage
{
    public AppLanguage Language { get; set; } = AppLanguage.English;
}

/// <summary>The caller's organization scope: one decision for every key (global by default), with the keys asked.</summary>
internal sealed class FakeOrganizationScope : IOrganizationScope
{
    public static readonly ScopeDecision Global = new(Allowed: true, Scope: PermissionScope.Global, OrganizationId: null);

    public ScopeDecision Decision { get; set; } = Global;

    public List<string> Keys { get; } = [];

    public static ScopeDecision Own(long? organizationId) => new(Allowed: true, Scope: PermissionScope.OwnOrganization, OrganizationId: organizationId);

    public Task<ScopeDecision> ScopeForAsync(string permissionKey, CancellationToken cancellationToken)
    {
        Keys.Add(permissionKey);
        return Task.FromResult(Decision);
    }
}

/// <summary>The Access privilege checks: the caller covers everyone and nobody is the last manager unless told otherwise.</summary>
internal sealed class FakeAdministrationGuard : IAdministrationGuard
{
    public HashSet<Guid> Outranking { get; } = [];

    public HashSet<long> LastManagers { get; } = [];

    public Task<bool> CallerCoversAsync(Guid targetSubjectId, CancellationToken cancellationToken) =>
        Task.FromResult(!Outranking.Contains(targetSubjectId));

    public Task<bool> IsLastAccessManagerAsync(long userId, CancellationToken cancellationToken) =>
        Task.FromResult(LastManagers.Contains(userId));
}

internal static class TestClock
{
    public static readonly DateTimeOffset Start = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
}

/// <summary>Sets the database-generated id of an entity (the fakes play the database).</summary>
internal static class Ids
{
    public static void Assign(object entity, long id) =>
        entity.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!.SetValue(entity, id);
}

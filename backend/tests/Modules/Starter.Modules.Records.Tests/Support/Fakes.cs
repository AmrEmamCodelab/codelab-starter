using System.Reflection;
using Starter.Application.Attachments;
using Starter.Application.Authorization;
using Starter.Application.Notifications;
using Starter.Application.Numbering;
using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Application.Users;
using Starter.Modules.Records.Application;
using Starter.Modules.Records.Domain;
using Starter.Modules.Records.Infrastructure.Persistence;

namespace Starter.Modules.Records.Tests.Support;

/// <summary>An in-memory <see cref="IRecordStore"/>: the same instances come back (tracked), ids are assigned on insert.</summary>
internal sealed class FakeRecordStore : IRecordStore
{
    private static readonly PropertyInfo RecordIdProperty = typeof(Record).GetProperty(nameof(Record.Id))!;
    private readonly List<RecordHistory> _staged = [];
    private long _nextId = 1;

    public List<Record> Records { get; } = [];

    public List<RecordHistory> History { get; } = [];

    /// <summary>True to make the next <see cref="TrySaveAsync"/> lose a concurrency race.</summary>
    public bool LoseNextSave { get; set; }

    public int Saves { get; private set; }

    public Task<Record?> FindAsync(long recordId, CancellationToken cancellationToken) =>
        Task.FromResult(Records.SingleOrDefault(record => record.Id == recordId));

    public Task<IReadOnlyList<Record>> ListAsync(IReadOnlyCollection<long> recordIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Record>>([.. Records.Where(record => recordIds.Contains(record.Id))]);

    public Task<PagedResult<Record>> PageAsync(RecordPageQuery query, CancellationToken cancellationToken)
    {
        var filtered = RecordFilters.Apply(Records.AsQueryable(), query).OrderByDescending(record => record.Id).ToList();
        var filter = query.Filter;
        var items = filtered.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();
        return Task.FromResult(new PagedResult<Record>(items, filter.Page, filter.PageSize, filtered.Count));
    }

    public Task<IReadOnlyList<RecordHistory>> HistoryAsync(long recordId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RecordHistory>>([.. History.Where(entry => entry.RecordId == recordId).OrderBy(entry => entry.At)]);

    public Task<RecordHistory?> LatestHistoryAsync(long recordId, CancellationToken cancellationToken) =>
        Task.FromResult(History.Where(entry => entry.RecordId == recordId).OrderBy(entry => entry.At).LastOrDefault());

    public Task<long> AddDraftAsync(Record record, Func<long, RecordHistory> firstEntry, CancellationToken cancellationToken)
    {
        var id = _nextId++;
        RecordIdProperty.SetValue(record, id);
        Records.Add(record);
        History.Add(firstEntry(id));
        return Task.FromResult(id);
    }

    public void Add(RecordHistory entry) => _staged.Add(entry);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException("Use TrySaveAsync.");

    public Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        Saves++;
        if (LoseNextSave)
        {
            LoseNextSave = false;
            _staged.Clear();
            return Task.FromResult(false);
        }

        History.AddRange(_staged);
        _staged.Clear();
        return Task.FromResult(true);
    }
}

/// <summary>One test user: identity, organization and the permission decisions they hold.</summary>
internal sealed record TestUser(long UserId, Guid Subject, string DisplayName, long? OrganizationId)
{
    public Dictionary<string, ScopeDecision> Grants { get; } = new(StringComparer.Ordinal);

    public TestUser Holds(string key, PermissionScope scope = PermissionScope.Global)
    {
        Grants[key] = new ScopeDecision(Allowed: true, scope, scope == PermissionScope.OwnOrganization ? OrganizationId : null);
        return this;
    }
}

/// <summary>Who is calling (switch with <see cref="SignIn"/>), plus the directory, scope and account ports over the users.</summary>
internal sealed class FakeUsers : ICurrentUserContext, IUserDirectory, IOrganizationScope
{
    private readonly List<TestUser> _users = [];

    public TestUser? Current { get; private set; }

    public bool IsAuthenticated => Current is not null;

    public Guid? SubjectId => Current?.Subject;

    public string? SessionId => null;

    public TestUser Add(long userId, string displayName, long? organizationId)
    {
        var user = new TestUser(userId, Guid.NewGuid(), displayName, organizationId);
        _users.Add(user);
        return user;
    }

    public void SignIn(TestUser? user) => Current = user;

    public Task<DirectoryUser?> FindBySubjectAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        var user = _users.SingleOrDefault(candidate => candidate.Subject == subjectId);
        return Task.FromResult(user is null
            ? null
            : new DirectoryUser(user.UserId, IsActive: true, HoldsAllPermissions: false, OrganizationId: user.OrganizationId, DisplayName: user.DisplayName));
    }

    public Task<ScopeDecision> ScopeForAsync(string permissionKey, CancellationToken cancellationToken) =>
        Task.FromResult(Current?.Grants.GetValueOrDefault(permissionKey) ?? ScopeDecision.Denied);

    public string? NameOf(long userId) => _users.SingleOrDefault(user => user.UserId == userId)?.DisplayName;
}

/// <summary>The account port: only <see cref="FindAsync"/> is used by Records.</summary>
internal sealed class FakeAccounts(FakeUsers users) : IAccountAdministration
{
    public Task<AccountSummary?> FindAsync(long userId, CancellationToken cancellationToken) =>
        Task.FromResult(users.NameOf(userId) is { } name
            ? new AccountSummary(userId, Guid.NewGuid(), name, name, null, null, null, Starter.Domain.Users.UserKind.External, Starter.Domain.Users.AccountBinding.Sso, null, true, true, false, false)
            : null);

    public Task<Outcome<AccountSummary>> CreateAsync(NewAccount account, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Outcome<AccountSummary>> UpdateAsync(long userId, AccountChanges changes, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Outcome<Done>> SetActiveAsync(long userId, bool isActive, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Outcome<Done>> UnlockAsync(long userId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<AccountPage> ListAsync(AccountQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Outcome<ActivationIssue>> IssueActivationAsync(long userId, CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>Records every notification; can be made to fail.</summary>
internal sealed class FakeNotifications : INotificationService
{
    public List<UserNotification> Sent { get; } = [];

    public bool Fail { get; set; }

    public Task NotifyUserAsync(UserNotification notification, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("The notification store is down.");
        }

        Sent.Add(notification);
        return Task.CompletedTask;
    }

    public Task SendToAddressAsync(AddressedNotification notification, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SendImmediateEmailAsync(UserNotification notification, CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>The interlock: the record ids that still miss a required file.</summary>
internal sealed class FakeRequiredAttachments : IRequiredAttachments
{
    public HashSet<long> Missing { get; } = [];

    public List<(string RecordType, long RecordId)> Asked { get; } = [];

    public Task<IReadOnlyList<string>> FindMissingTypeKeysAsync(string recordType, long recordId, CancellationToken cancellationToken)
    {
        Asked.Add((recordType, recordId));
        return Task.FromResult<IReadOnlyList<string>>(Missing.Contains(recordId) ? ["test-document"] : []);
    }
}

/// <summary>A counter per prefix in a fixed year.</summary>
internal sealed class FakeNumbers : IRequestNumberAllocator
{
    public const int Year = 2026;

    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

    public int Taken { get; private set; }

    public Task<string> NextAsync(string prefix, CancellationToken cancellationToken)
    {
        Taken++;
        var next = _counters.GetValueOrDefault(prefix) + 1;
        _counters[prefix] = next;
        return Task.FromResult(RequestNumberFormat.Format(prefix, Year, next));
    }
}

/// <summary>A submit validator that answers fixed field errors.</summary>
internal sealed class FakeSubmitValidator(string recordType) : IRecordSubmitValidator
{
    public List<FieldError> Errors { get; } = [];

    public string RecordType => recordType;

    public Task<IReadOnlyList<FieldError>> ValidateForSubmitAsync(long recordId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FieldError>>([.. Errors]);
}

/// <summary>Records every transition event; can be made to fail.</summary>
internal sealed class FakeObserver : IRecordTransitionObserver
{
    public List<RecordTransitioned> Seen { get; } = [];

    public bool Fail { get; set; }

    public Task OnTransitionedAsync(RecordTransitioned transition, CancellationToken cancellationToken)
    {
        Seen.Add(transition);
        return Fail ? throw new InvalidOperationException("The observer broke.") : Task.CompletedTask;
    }
}

/// <summary>A fixed assignment per record.</summary>
internal sealed class FakeAssignments : IRecordAssignmentReader
{
    public Dictionary<long, AssignmentSnapshot> Current { get; } = [];

    public Task<IReadOnlyDictionary<long, AssignmentSnapshot>> CurrentAsync(IReadOnlyCollection<long> recordIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<long, AssignmentSnapshot>>(Current.Where(entry => recordIds.Contains(entry.Key)).ToDictionary());
}

/// <summary>A fixed SLA badge per record.</summary>
internal sealed class FakeSla : IRecordSlaReader
{
    public Dictionary<long, SlaBadge> Badges { get; } = [];

    public Task<IReadOnlyDictionary<long, SlaBadge>> BadgesAsync(IReadOnlyCollection<long> recordIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<long, SlaBadge>>(Badges.Where(entry => recordIds.Contains(entry.Key)).ToDictionary());
}

/// <summary>A fixed set of registrations.</summary>
internal sealed class FakeRegistrations(params RecordTypeRegistration[] registrations) : IRecordTypeRegistrationSource
{
    public IEnumerable<RecordTypeRegistration> Registrations => registrations;
}

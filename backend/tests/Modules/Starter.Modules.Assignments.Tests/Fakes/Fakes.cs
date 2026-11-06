using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Application.Users;
using Starter.Domain.Users;
using Starter.Modules.Assignments.Application;
using Starter.Modules.Assignments.Domain;

namespace Starter.Modules.Assignments.Tests.Fakes;

/// <summary>
/// An in-memory record directory with the kernel query semantics: every filter ANDs, newest submit first (drafts last),
/// 1-based pages. Records how often it was queried.
/// </summary>
internal sealed class FakeRecordDirectory : IRecordDirectory
{
    public List<RecordSummary> Records { get; } = [];

    public List<RecordQuery> Queries { get; } = [];

    public Task<IReadOnlyList<RecordSummary>> GetAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken)
    {
        IReadOnlyList<RecordSummary> found = [.. Records.Where(record => ids.Contains(record.Id))];
        return Task.FromResult(found);
    }

    public Task<PagedResult<RecordSummary>> QueryAsync(RecordQuery query, CancellationToken cancellationToken)
    {
        Queries.Add(query);
        var matching = Records.Where(record => Matches(record, query))
            .OrderByDescending(record => record.SubmittedAt.HasValue)
            .ThenByDescending(record => record.SubmittedAt ?? record.CreatedAt)
            .ThenByDescending(record => record.Id)
            .ToList();
        IReadOnlyList<RecordSummary> page = [.. matching.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)];
        return Task.FromResult(new PagedResult<RecordSummary>(page, query.Page, query.PageSize, matching.Count));
    }

    private static bool Matches(RecordSummary record, RecordQuery query) =>
        record.RecordType == query.RecordType
        && (query.Statuses is null || query.Statuses.Contains(record.Status))
        && (query.NumberContains is null || (record.Number?.Contains(query.NumberContains, StringComparison.OrdinalIgnoreCase) ?? false))
        && (query.OrganizationId is null || record.OrganizationId == query.OrganizationId)
        && (query.SubmittedFrom is null || record.SubmittedAt >= query.SubmittedFrom)
        && (query.SubmittedTo is null || record.SubmittedAt < query.SubmittedTo)
        && (query.Ids is null || query.Ids.Contains(record.Id));
}

/// <summary>
/// An in-memory assignment store that enforces "one open row per record" on save, like the filtered unique index.
/// <see cref="ConcurrentClaimOnNextSave"/> simulates another request opening a row just before the next save.
/// </summary>
internal sealed class FakeAssignmentStore : IAssignmentStore
{
    private readonly List<RecordAssignment> _staged = [];

    public List<RecordAssignment> Rows { get; } = [];

    public int Saves { get; private set; }

    /// <summary>Set to a row that "another request" commits right before the next save.</summary>
    public RecordAssignment? ConcurrentClaimOnNextSave { get; set; }

    public Task<RecordAssignment?> FindOpenAsync(long recordId, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.SingleOrDefault(row => row.RecordId == recordId && row.IsOpen));

    public Task<IReadOnlyList<RecordAssignment>> ListOpenAsync(IReadOnlyCollection<long> recordIds, CancellationToken cancellationToken)
    {
        IReadOnlyList<RecordAssignment> open = [.. Rows.Where(row => recordIds.Contains(row.RecordId) && row.IsOpen)];
        return Task.FromResult(open);
    }

    public Task<IReadOnlyList<long>> OpenRecordIdsAsync(OpenAssignmentFilter filter, CancellationToken cancellationToken)
    {
        IReadOnlyList<long> ids = [.. Rows
            .Where(row => row.IsOpen && row.RecordType == filter.RecordType)
            .Where(row => filter.AssigneeUserId is null || row.AssigneeUserId == filter.AssigneeUserId)
            .Select(row => row.RecordId)];
        return Task.FromResult(ids);
    }

    public void Add(RecordAssignment assignment) => _staged.Add(assignment);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        var written = _staged.Count;
        Rows.AddRange(_staged);
        _staged.Clear();
        return Task.FromResult(written);
    }

    public Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        ApplyConcurrentClaim();
        if (_staged.Any(staged => Rows.Any(row => row.IsOpen && row.RecordId == staged.RecordId)))
        {
            _staged.Clear();
            return Task.FromResult(false);
        }

        Saves++;
        Rows.AddRange(_staged);
        _staged.Clear();
        return Task.FromResult(true);
    }

    public async Task<bool> TryReassignAsync(RecordAssignment replacement, CancellationToken cancellationToken)
    {
        Add(replacement);
        return await TrySaveAsync(cancellationToken);
    }

    public RecordAssignment Seed(RecordAssignment row)
    {
        Rows.Add(row);
        return row;
    }

    private void ApplyConcurrentClaim()
    {
        if (ConcurrentClaimOnNextSave is { } claim)
        {
            Rows.Add(claim);
            ConcurrentClaimOnNextSave = null;
        }
    }
}

/// <summary>The current user's scope decision per permission key (denied unless set).</summary>
internal sealed class FakeOrganizationScope : IOrganizationScope
{
    public Dictionary<string, ScopeDecision> Decisions { get; } = new(StringComparer.Ordinal);

    public Task<ScopeDecision> ScopeForAsync(string permissionKey, CancellationToken cancellationToken) =>
        Task.FromResult(Decisions.GetValueOrDefault(permissionKey, ScopeDecision.Denied));
}

/// <summary>Another user's scope decision per (user, key) (denied unless set).</summary>
internal sealed class FakeAssigneeEligibility : IAssigneeEligibility
{
    public Dictionary<(long UserId, string Key), ScopeDecision> Decisions { get; } = [];

    public Task<ScopeDecision> ScopeForUserAsync(long userId, string permissionKey, CancellationToken cancellationToken) =>
        Task.FromResult(Decisions.GetValueOrDefault((userId, permissionKey), ScopeDecision.Denied));
}

/// <summary>The caller (a token subject, or anonymous) and the users the directory knows.</summary>
internal sealed class FakeUsers : ICurrentUserContext, IUserDirectory, IAccountAdministration
{
    private readonly Dictionary<Guid, DirectoryUser> _bySubject = [];
    private readonly Dictionary<long, string> _names = [];

    public Guid? SubjectId { get; private set; }

    public bool IsAuthenticated => SubjectId is not null;

    public string? SessionId => SubjectId?.ToString("N");

    /// <summary>Adds an active user and returns its subject.</summary>
    public Guid Add(long userId, string displayName, bool isActive = true)
    {
        var subject = Guid.NewGuid();
        _bySubject[subject] = new DirectoryUser(userId, isActive, HoldsAllPermissions: false, UserKind.External, OrganizationId: null, displayName);
        _names[userId] = displayName;
        return subject;
    }

    public void SignIn(Guid subject) => SubjectId = subject;

    public void SignOut() => SubjectId = null;

    public Task<DirectoryUser?> FindBySubjectAsync(Guid subjectId, CancellationToken cancellationToken) =>
        Task.FromResult(_bySubject.GetValueOrDefault(subjectId));

    public Task<AccountSummary?> FindAsync(long userId, CancellationToken cancellationToken) =>
        Task.FromResult(_names.TryGetValue(userId, out var name) ? Account(userId, name) : null);

    public Task<Outcome<AccountSummary>> CreateAsync(NewAccount account, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Outcome<AccountSummary>> UpdateAsync(long userId, AccountChanges changes, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Outcome<Done>> SetActiveAsync(long userId, bool isActive, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Outcome<Done>> UnlockAsync(long userId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<AccountPage> ListAsync(AccountQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Outcome<ActivationIssue>> IssueActivationAsync(long userId, CancellationToken cancellationToken) => throw new NotSupportedException();

    private static AccountSummary? Account(long userId, string displayName) =>
        new(
            UserId: userId,
            SubjectId: Guid.Empty,
            UserName: "user" + userId,
            DisplayName: displayName,
            Email: null,
            LoginName: null,
            Upn: null,
            Kind: UserKind.External,
            Binding: AccountBinding.Sso,
            OrganizationId: null,
            IsActive: true,
            HasPassword: false,
            IsLockedOut: false,
            IsBreakGlass: false);
}

/// <summary>Fixed SLA badges per record id.</summary>
internal sealed class FakeSlaReader : IRecordSlaReader
{
    public Dictionary<long, SlaBadge> Badges { get; } = [];

    public Task<IReadOnlyDictionary<long, SlaBadge>> BadgesAsync(IReadOnlyCollection<long> recordIds, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<long, SlaBadge> found = Badges.Where(pair => recordIds.Contains(pair.Key)).ToDictionary();
        return Task.FromResult(found);
    }
}

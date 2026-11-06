using System.Reflection;
using Starter.Application.Notifications;
using Starter.Application.Records;
using Starter.Application.Workflow;
using Starter.Domain.Workflow;
using Starter.Modules.Deadlines.Application;
using Starter.Modules.Deadlines.Domain;

namespace Starter.Modules.Deadlines.Tests.Fakes;

/// <summary>The test-only record type the generic tests use (never the sample).</summary>
internal static class TestRecord
{
    public const string Type = "test-record";
    public const string OtherType = "other-record";
    public const string Prefix = "TR";
    public const string QueuePermission = "test-records.queue";
    public const string ViewOwnPermission = "test-records.view-own";
    public const string EscalationTemplate = "test-records.sla-escalation";

    public const int Draft = 1;
    public const int Submitted = 2;
    public const int UnderReview = 3;
    public const int Returned = 4;
    public const int Closed = 9;

    public const string UnderReviewKey = "under-review";

    public static readonly TimeSpan ReviewSla = TimeSpan.FromDays(3);
    public static readonly TimeSpan ReviewDueSoon = TimeSpan.FromDays(1);
    public static readonly DateTimeOffset Start = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    public static SlaDefinition ReviewDefinition(string recordType = Type) =>
        new(
            RecordType: recordType,
            Status: UnderReview,
            Duration: ReviewSla,
            DueSoonBefore: ReviewDueSoon,
            EscalationTemplateKey: EscalationTemplate);

    public static RecordTypeRegistration Registration() =>
        new(
            RecordType: Type,
            NumberPrefix: Prefix,
            QueuePermissionKey: QueuePermission,
            ViewOwnPermissionKey: ViewOwnPermission,
            AttachableRecordType: Type);

    public static RecordSummary Summary(long id, int status = UnderReview, long? organizationId = null, bool isTerminal = false) =>
        new(
            Id: id,
            RecordType: Type,
            Number: $"{Prefix}-2026-{id:000}",
            Status: status,
            OwnerUserId: 500,
            OrganizationId: organizationId,
            CreatedAt: Start,
            SubmittedAt: Start,
            UpdatedAt: Start,
            IsTerminal: isTerminal);

    public static RecordTransitioned Transition(long recordId, int from, int to, DateTimeOffset at, bool toTerminal = false) =>
        new(
            RecordId: recordId,
            RecordType: Type,
            FromStatus: from,
            ToStatus: to,
            ActionKey: "move",
            ActorUserId: 500,
            ToTerminal: toTerminal,
            At: at);

    public static TransitionCatalog Workflow() =>
        new(
            recordType: Type,
            statuses:
            [
                Status(Draft, "draft", isInitial: true, isTerminal: false),
                Status(Submitted, "submitted", isInitial: false, isTerminal: false),
                Status(UnderReview, UnderReviewKey, isInitial: false, isTerminal: false),
                Status(Returned, "returned", isInitial: false, isTerminal: false),
                Status(Closed, "closed", isInitial: false, isTerminal: true),
            ],
            rules: []);

    private static WorkflowStatusDefinition Status(int value, string key, bool isInitial, bool isTerminal) =>
        new(value, key, isInitial, isTerminal, new StatusWording(RequesterKey: key + ".r", StaffKey: key + ".s", AdminKey: key + ".a"));
}

internal sealed class PolicySource(params SlaDefinition[] definitions) : ISlaPolicySource
{
    public IEnumerable<SlaDefinition> Definitions { get; } = definitions;
}

internal sealed class RegistrationSource(params RecordTypeRegistration[] registrations) : IRecordTypeRegistrationSource
{
    public IEnumerable<RecordTypeRegistration> Registrations { get; } = registrations;
}

internal sealed class FakeWorkflows(params TransitionCatalog[] catalogs) : IRecordWorkflowCatalogs
{
    public IReadOnlyList<TransitionCatalog> All { get; } = catalogs;

    public TransitionCatalog? Find(string recordType) => All.FirstOrDefault(catalog => catalog.RecordType == recordType);
}

/// <summary>An in-memory deadline store that assigns ids like the identity column and counts saves.</summary>
internal sealed class InMemoryDeadlineStore : IRecordDeadlineStore
{
    private static readonly PropertyInfo IdProperty = typeof(RecordDeadline).GetProperty(nameof(RecordDeadline.Id))!;
    private readonly List<RecordDeadline> _pending = [];
    private long _nextId = 1;

    public List<RecordDeadline> Saved { get; } = [];

    public int Saves { get; private set; }

    public IEnumerable<RecordDeadline> Open => Saved.Where(deadline => deadline.IsOpen);

    public Task<RecordDeadline?> FindOpenAsync(long recordId, string recordType, CancellationToken cancellationToken) =>
        Task.FromResult(Saved.FirstOrDefault(deadline => deadline.RecordId == recordId && deadline.RecordType == recordType && deadline.IsOpen));

    public Task<IReadOnlyList<RecordDeadline>> OpenForAsync(IReadOnlyCollection<long> recordIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RecordDeadline>>([.. Open.Where(deadline => recordIds.Contains(deadline.RecordId))]);

    public Task<IReadOnlyList<RecordDeadline>> DueForBreachAsync(DateTimeOffset now, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RecordDeadline>>([.. Open.Where(deadline => deadline.IsUnmarkedBreachAt(now)).OrderBy(deadline => deadline.DueAt).Take(take)]);

    public Task<IReadOnlyList<RecordDeadline>> AwaitingEscalationAsync(int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RecordDeadline>>([.. Saved.Where(deadline => deadline.AwaitsEscalation).OrderBy(deadline => deadline.DueAt).Take(take)]);

    public void Add(RecordDeadline deadline) => _pending.Add(deadline);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        foreach (var deadline in _pending)
        {
            IdProperty.SetValue(deadline, _nextId++);
            Saved.Add(deadline);
        }

        _pending.Clear();
        Saves++;
        return Task.CompletedTask;
    }

    public RecordDeadline Seed(long recordId, DateTimeOffset startedAt, SlaDefinition? definition = null)
    {
        Add(RecordDeadline.Open(recordId, definition ?? TestRecord.ReviewDefinition(), startedAt));
        SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
        return Saved[^1];
    }
}

internal sealed class FakeNotifications : INotificationService
{
    public List<UserNotification> Sent { get; } = [];

    /// <summary>Throws for these user ids (before anything is recorded for them).</summary>
    public HashSet<long> FailFor { get; } = [];

    public Task NotifyUserAsync(UserNotification notification, CancellationToken cancellationToken)
    {
        if (FailFor.Contains(notification.UserId))
        {
            throw new InvalidOperationException("Template missing (test).");
        }

        Sent.Add(notification);
        return Task.CompletedTask;
    }

    public Task SendToAddressAsync(AddressedNotification notification, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SendImmediateEmailAsync(UserNotification notification, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class FakeRecordDirectory : IRecordDirectory
{
    public Dictionary<long, RecordSummary> Records { get; } = [];

    public Task<IReadOnlyList<RecordSummary>> GetAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RecordSummary>>([.. ids.Where(Records.ContainsKey).Select(id => Records[id])]);

    public Task<PagedResult<RecordSummary>> QueryAsync(RecordQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class FakeAssignments : IRecordAssignmentReader
{
    public Dictionary<long, AssignmentSnapshot> Current { get; } = [];

    public int Calls { get; private set; }

    public Task<IReadOnlyDictionary<long, AssignmentSnapshot>> CurrentAsync(IReadOnlyCollection<long> recordIds, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult<IReadOnlyDictionary<long, AssignmentSnapshot>>(
            Current.Where(pair => recordIds.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    public void Assign(long recordId, long userId) =>
        Current[recordId] = new AssignmentSnapshot(AssigneeUserId: userId, AssigneeDisplayName: "Assignee", AssignedAt: TestRecord.Start);
}

internal sealed class FakeHolders : IPermissionHolderDirectory
{
    public Dictionary<(string Key, long? OrganizationId), long[]> Holders { get; } = [];

    public List<(string Key, long? OrganizationId)> Calls { get; } = [];

    public Task<IReadOnlyList<long>> HoldersAsync(string permissionKey, long? organizationId, CancellationToken cancellationToken)
    {
        Calls.Add((permissionKey, organizationId));
        return Task.FromResult<IReadOnlyList<long>>(Holders.GetValueOrDefault((permissionKey, organizationId), []));
    }
}

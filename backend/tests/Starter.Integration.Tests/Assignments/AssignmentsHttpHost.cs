using System.Collections.Concurrent;
using Codelab.Security.Keys;
using Codelab.Security.Tickets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Application.Workflow;
using Starter.Domain.Workflow;
using Starter.Infrastructure.Workflow;
using Starter.Integration.Tests.Support;
using Starter.Modules.Users.Infrastructure.Options;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests.Assignments;

/// <summary>
/// The Development host for the queue tests: the real Assignments module on its own throwaway database, with the
/// test-only record type <c>test-record</c> (prefix TR: registration, workflow, permissions) and an in-memory
/// <see cref="IRecordDirectory"/> (<see cref="Directory"/>) instead of the Records module's, so the tests place records
/// in any status and organization. Activation tickets are on so the tests can create staff users who sign in.
/// </summary>
public sealed class AssignmentsHttpHost() : IntegrationHost(Environments.Development)
{
    /// <summary>The web origin the activation links point at.</summary>
    public const string LinkBase = "http://localhost:4280";

    /// <summary>The records the queues see, shared by every test of the collection (use your own ids).</summary>
    public InMemoryRecordDirectory Directory { get; } = new();

    /// <summary>A service of the started host.</summary>
    public T Service<T>()
        where T : notnull => Factory.Services.GetRequiredService<T>();

    /// <inheritdoc />
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.PostConfigure<ActivationOptions>(options => options.LinkBaseUrl = LinkBase);
        var keyId = StarterTestAccounts.NewKeyId();
        services.PostConfigure<SealedTicketOptions>(options =>
        {
            options.Enabled = true;
            options.ActiveKeyId = keyId;
            options.Keys.Clear();
            options.Keys.Add(new KeyEntryOptions { KeyId = keyId, MaterialBase64 = StarterTestAccounts.NewKeyBase64() });
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, TestRecordPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRecordTypeRegistrationSource, TestRecordRegistrations>());
        services.AddRecordWorkflow<TestRecordWorkflow>();
        services.RemoveAll<IRecordDirectory>();
        services.AddSingleton<IRecordDirectory>(Directory);
    }
}

/// <summary>Tests sharing the queue host.</summary>
[CollectionDefinition(Name)]
public sealed class AssignmentsHttpHostGroup : ICollectionFixture<AssignmentsHttpHost>
{
    /// <summary>The collection name.</summary>
    public const string Name = "assignments host";
}

/// <summary>The keys and statuses of the test-only record type.</summary>
public static class TestRecord
{
    public const string Type = "test-record";
    public const string Prefix = "TR";
    public const string QueuePermission = "test-records.queue";
    public const string ViewOwnPermission = "test-records.view-own";
    public const string ActPermission = "test-records.act";
    public const int Draft = 1;
    public const int Submitted = 2;
    public const int InReview = 3;
    public const int Closed = 4;
}

/// <summary>The test record type's permissions (so the Access module can grant them).</summary>
internal sealed class TestRecordPermissions : IPermissionCatalogSource
{
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(Key: TestRecord.QueuePermission, Description: "See the test record queue (integration tests only)."),
        new(Key: TestRecord.ViewOwnPermission, Description: "See your own test records (integration tests only)."),
        new(Key: TestRecord.ActPermission, Description: "Move test records (integration tests only)."),
    ];
}

/// <summary>The test record type's registration.</summary>
internal sealed class TestRecordRegistrations : IRecordTypeRegistrationSource
{
    public IEnumerable<RecordTypeRegistration> Registrations { get; } =
        [new RecordTypeRegistration(TestRecord.Type, TestRecord.Prefix, TestRecord.QueuePermission, TestRecord.ViewOwnPermission, AttachableRecordType: TestRecord.Type)];
}

/// <summary>The test record type's workflow: draft, submitted, in review, closed (terminal); valid for the startup check.</summary>
internal sealed class TestRecordWorkflow : IRecordWorkflowDefinition
{
    public string RecordType => TestRecord.Type;

    public TransitionCatalog Catalog { get; } = new(
        TestRecord.Type,
        [
            Status(TestRecord.Draft, "draft", isInitial: true, isTerminal: false),
            Status(TestRecord.Submitted, "submitted", isInitial: false, isTerminal: false),
            Status(TestRecord.InReview, "in-review", isInitial: false, isTerminal: false),
            Status(TestRecord.Closed, "closed", isInitial: false, isTerminal: true),
        ],
        [
            Rule("submit", TestRecord.Draft, TestRecord.Submitted, TransitionSide.Requester),
            Rule("start", TestRecord.Submitted, TestRecord.InReview, TransitionSide.Staff),
            Rule("close", TestRecord.InReview, TestRecord.Closed, TransitionSide.Staff),
        ]);

    private static WorkflowStatusDefinition Status(int value, string key, bool isInitial, bool isTerminal) =>
        new(value, key, isInitial, isTerminal, new StatusWording($"records.{key}.requester", $"records.{key}.staff", $"records.{key}.admin"));

    private static TransitionRule Rule(string action, int from, int to, TransitionSide side) =>
        new(
            ActionKey: action,
            FromStatus: from,
            ToStatus: to,
            PermissionKey: TestRecord.ActPermission,
            Side: side,
            Reason: ReasonRequirement.None,
            ReasonFieldKey: null,
            LabelKey: $"records.action.{action}",
            Notification: NotificationDecision.None,
            RequiresOwnership: side == TransitionSide.Requester);
}

/// <summary>
/// A thread-safe in-memory <see cref="IRecordDirectory"/> with the kernel query semantics (every filter ANDs, newest
/// submit first, 1-based pages).
/// </summary>
public sealed class InMemoryRecordDirectory : IRecordDirectory
{
    private readonly ConcurrentDictionary<long, RecordSummary> _records = new();

    /// <summary>Adds or replaces a record.</summary>
    public void Put(RecordSummary record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _records[record.Id] = record;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RecordSummary>> GetAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken)
    {
        IReadOnlyList<RecordSummary> found = [.. ids.Where(_records.ContainsKey).Select(id => _records[id])];
        return Task.FromResult(found);
    }

    /// <inheritdoc />
    public Task<PagedResult<RecordSummary>> QueryAsync(RecordQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var matching = _records.Values.Where(record => Matches(record, query))
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

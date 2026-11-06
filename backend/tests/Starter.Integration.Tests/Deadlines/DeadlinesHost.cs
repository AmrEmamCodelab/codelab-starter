using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Infrastructure.Workflow;
using Starter.Integration.Tests.Assignments;
using Starter.Integration.Tests.Support;

namespace Starter.Integration.Tests.Deadlines;

/// <summary>
/// The test-only record type of the SLA proof (prefix TR), its 3-day SLA on "under review" and its escalation template.
/// Generic tests never use the sample record type.
/// </summary>
internal static class TestRecordSla
{
    /// <summary>The record type key.</summary>
    public const string RecordType = "test-record";

    /// <summary>The number prefix.</summary>
    public const string Prefix = "TR";

    /// <summary>The status with the SLA.</summary>
    public const int UnderReview = 3;

    /// <summary>A status after review (no SLA).</summary>
    public const int Returned = 4;

    /// <summary>The status before review.</summary>
    public const int Submitted = 2;

    /// <summary>The escalation template the test seeds into the host's database.</summary>
    public const string EscalationTemplate = "test-records.sla-escalation";

    /// <summary>The job key of the Deadlines scan (<c>DeadlinesJobKeys.SlaScan</c>; repeated here so this file needs only kernel types).</summary>
    public const string ScanJobKey = "deadlines-sla-scan";

    /// <summary>The SLA.</summary>
    public static readonly TimeSpan Review = TimeSpan.FromDays(3);

    /// <summary>The due-soon window.</summary>
    public static readonly TimeSpan ReviewDueSoon = TimeSpan.FromDays(1);

    /// <summary>The definition the fake policy source declares.</summary>
    public static SlaDefinition Definition { get; } = new(
        RecordType: RecordType,
        Status: UnderReview,
        Duration: Review,
        DueSoonBefore: ReviewDueSoon,
        EscalationTemplateKey: EscalationTemplate);
}

/// <summary>
/// The phase 5 Development host (real SQL, shiftable clock, file-drop e-mail, no job server) plus the stand-ins of the
/// record kernel for a test-only record type: an <see cref="ISlaPolicySource"/> with its 3-day SLA, a record type
/// registration with its workflow and permissions (the host refuses a registered record type without a workflow,
/// because the Records module is in the host), an in-memory <see cref="IRecordDirectory"/> (<see cref="Records"/>) and an in-memory
/// <see cref="IRecordAssignmentReader"/> (<see cref="Assignees"/>). The Deadlines module itself is the real one (its
/// observer, store, scan job and badge reader on the test database).
/// </summary>
public sealed class DeadlinesHost : Phase5Host
{
    /// <summary>The records the directory answers, by internal id.</summary>
    public ConcurrentDictionary<long, RecordSummary> Records { get; } = new();

    /// <summary>The current assignee of each record, by internal id.</summary>
    public ConcurrentDictionary<long, long> Assignees { get; } = new();

    /// <inheritdoc />
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton<ISlaPolicySource>(new TestPolicy());
        services.AddSingleton<IRecordTypeRegistrationSource>(new TestRegistration());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, TestRecordPermissions>());
        services.AddRecordWorkflow<TestRecordWorkflow>();
        services.RemoveAll<IRecordDirectory>();
        services.AddSingleton<IRecordDirectory>(new InMemoryDirectory(Records));
        services.RemoveAll<IRecordAssignmentReader>();
        services.AddSingleton<IRecordAssignmentReader>(new InMemoryAssignments(Assignees));
    }

    private sealed class TestPolicy : ISlaPolicySource
    {
        public IEnumerable<SlaDefinition> Definitions { get; } = [TestRecordSla.Definition];
    }

    private sealed class TestRegistration : IRecordTypeRegistrationSource
    {
        public IEnumerable<RecordTypeRegistration> Registrations { get; } =
        [
            new(
                RecordType: TestRecordSla.RecordType,
                NumberPrefix: TestRecordSla.Prefix,
                QueuePermissionKey: TestRecord.QueuePermission,
                ViewOwnPermissionKey: TestRecord.ViewOwnPermission,
                AttachableRecordType: TestRecordSla.RecordType),
        ];
    }

    private sealed class InMemoryDirectory(ConcurrentDictionary<long, RecordSummary> records) : IRecordDirectory
    {
        public Task<IReadOnlyList<RecordSummary>> GetAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RecordSummary>>([.. ids.Where(records.ContainsKey).Select(id => records[id])]);

        public Task<PagedResult<RecordSummary>> QueryAsync(RecordQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The SLA proof only reads records by id.");
    }

    private sealed class InMemoryAssignments(ConcurrentDictionary<long, long> assignees) : IRecordAssignmentReader
    {
        public Task<IReadOnlyDictionary<long, AssignmentSnapshot>> CurrentAsync(IReadOnlyCollection<long> recordIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<long, AssignmentSnapshot>>(recordIds
                .Where(assignees.ContainsKey)
                .ToDictionary(id => id, id => new AssignmentSnapshot(assignees[id], AssigneeDisplayName: "Assignee", AssignedAt: DateTimeOffset.MinValue)));
    }
}

/// <summary>Tests sharing the deadlines host.</summary>
[CollectionDefinition(Name)]
public sealed class DeadlinesHostGroup : ICollectionFixture<DeadlinesHost>
{
    /// <summary>The collection name.</summary>
    public const string Name = "deadlines host";
}

using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Application.Workflow;
using Starter.Domain.Records;
using Starter.Domain.Workflow;
using Starter.Modules.Assignments.Application.Commands;
using Starter.Modules.Assignments.Application.Queues;
using Starter.Modules.Assignments.Domain;
using Starter.Modules.Assignments.Application;

namespace Starter.Modules.Assignments.Tests.Fakes;

/// <summary>The statuses of the test-only record type (prefix TR).</summary>
internal static class TestRecordStatuses
{
    public const int Draft = 1;
    public const int Submitted = 2;
    public const int InReview = 3;
    public const int Closed = 4;
}

/// <summary>The test-only record type <c>test-record</c>: its registration and its workflow catalog.</summary>
internal sealed class TestRecordType : IRecordTypeRegistrationSource, IRecordWorkflowDefinition
{
    public const string Key = "test-record";
    public const string QueuePermission = "test-records.queue";
    public const string ViewOwnPermission = "test-records.view-own";
    public const string Prefix = "TR";

    public static TestRecordType Instance { get; } = new();

    public IEnumerable<RecordTypeRegistration> Registrations { get; } =
        [new RecordTypeRegistration(Key, Prefix, QueuePermission, ViewOwnPermission, AttachableRecordType: Key)];

    public string RecordType => Key;

    public TransitionCatalog Catalog { get; } = new(
        Key,
        [
            Status(TestRecordStatuses.Draft, "draft", isInitial: true, isTerminal: false),
            Status(TestRecordStatuses.Submitted, "submitted", isInitial: false, isTerminal: false),
            Status(TestRecordStatuses.InReview, "in-review", isInitial: false, isTerminal: false),
            Status(TestRecordStatuses.Closed, "closed", isInitial: false, isTerminal: true),
        ],
        []);

    private static WorkflowStatusDefinition Status(int value, string key, bool isInitial, bool isTerminal) =>
        new(value, key, isInitial, isTerminal, new StatusWording($"records.{key}.requester", $"records.{key}.staff", $"records.{key}.admin"));
}

/// <summary>Every fake and the module's services wired over them, as the registration wires them in the host.</summary>
internal sealed class TestWorld
{
    public const long StaffUserId = 10;
    public const long OtherStaffUserId = 11;
    public const long OwnerUserId = 20;
    public const long OwnOrganization = 7;
    public const long OtherOrganization = 8;
    public const string StaffName = "Staff One";
    public const string OtherStaffName = "Staff Two";
    public const string OwnerName = "Requester One";

    public static readonly DateTimeOffset Now = new(2026, 11, 6, 9, 0, 0, TimeSpan.FromHours(3));

    public TestWorld()
    {
        StaffSubject = Users.Add(StaffUserId, StaffName);
        OtherStaffSubject = Users.Add(OtherStaffUserId, OtherStaffName);
        Users.Add(OwnerUserId, OwnerName);
        Users.SignIn(StaffSubject);
        Scope.Decisions[TestRecordType.QueuePermission] = Global;
    }

    public static ScopeDecision Global { get; } = new(Allowed: true, Scope: PermissionScope.Global, OrganizationId: null);

    public static ScopeDecision OwnOrganizationOnly { get; } = new(Allowed: true, Scope: PermissionScope.OwnOrganization, OrganizationId: OwnOrganization);

    public FakeRecordDirectory Directory { get; } = new();

    public FakeAssignmentStore Store { get; } = new();

    public FakeOrganizationScope Scope { get; } = new();

    public FakeUsers Users { get; } = new();

    public FakeAssigneeEligibility Eligibility { get; } = new();

    public FakeSlaReader Sla { get; } = new();

    public FakeTimeProvider Clock { get; } = new(Now);

    public Guid StaffSubject { get; }

    public Guid OtherStaffSubject { get; }

    public RecordQueues Queues => new([TestRecordType.Instance], new RecordWorkflowCatalogs([TestRecordType.Instance]), Scope, Directory);

    public CurrentStaff Staff => new(Users, Users);

    public UserDisplayNames Names => new(Users);

    public AssignmentRows Rows => new(Store, Clock, NullLogger<AssignmentRows>.Instance);

    public ClaimRecord Claim => new(Staff, Queues, Rows);

    public ReleaseRecord Release => new(Staff, Queues, Rows);

    public AssignRecord Assign => new(new AssignRecordValidator(), Staff, Queues, Eligibility, Rows);

    public CloseAssignmentOnTerminal Observer => new(Store, NullLogger<CloseAssignmentOnTerminal>.Instance);

    public RecordAssignmentReader Reader => new(Store, Names);

    public GetWorkQueuePage WorkQueue => new(
        new WorkQueueRequestValidator(),
        Staff,
        Queues,
        new WorkQueueViews(Directory, Store, new UnassignedQueuePages(Directory, Store)),
        new WorkQueueEntries(Reader, Names, [Sla]));

    public static QueueItemKey Item(long recordId) => new(TestRecordType.Key, recordId);

    public static WorkQueueRequest Request(string view, int? status = null, string? number = null, int? page = null, int? pageSize = null) =>
        new(TestRecordType.Key, view, status, number, SubmittedFrom: null, SubmittedTo: null, page, pageSize);

    /// <summary>Adds a record of the test type: submitted <paramref name="minutesAgo"/> minutes before <see cref="Now"/> unless a draft.</summary>
    public RecordSummary AddRecord(long id, int status = TestRecordStatuses.Submitted, long? organizationId = OwnOrganization, int minutesAgo = 0)
    {
        var isDraft = status == TestRecordStatuses.Draft;
        var record = new RecordSummary(
            Id: id,
            RecordType: TestRecordType.Key,
            Number: isDraft ? null : "TR-2026-" + id.ToString("000", CultureInfo.InvariantCulture),
            Status: status,
            OwnerUserId: OwnerUserId,
            OrganizationId: organizationId,
            CreatedAt: Now.AddDays(-1),
            SubmittedAt: isDraft ? null : Now.AddMinutes(-minutesAgo),
            UpdatedAt: Now,
            IsTerminal: status == TestRecordStatuses.Closed);
        Directory.Records.Add(record);
        return record;
    }

    /// <summary>Seeds an open row of <paramref name="recordId"/> held by <paramref name="assigneeUserId"/>.</summary>
    public RecordAssignment SeedOpen(long recordId, long assigneeUserId) =>
        Store.Seed(OpenRow(recordId, assigneeUserId));

    public static RecordAssignment OpenRow(long recordId, long assigneeUserId) =>
        RecordAssignment.Open(new AssignmentStart(new RecordRef(recordId, TestRecordType.Key), assigneeUserId, assigneeUserId, Now.AddHours(-1)));
}

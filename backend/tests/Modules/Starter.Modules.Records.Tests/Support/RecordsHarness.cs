using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Application.Workflow;
using Starter.Domain.Records;
using Starter.Domain.Workflow;
using Starter.Modules.Records.Application;
using Starter.Workflow.Testing;

namespace Starter.Modules.Records.Tests.Support;

/// <summary>
/// The test-only record type of these tests: the kit's <c>test-record</c> catalog with its withdraw rules keyed
/// <see cref="RecordLifecycleActions.Cancel"/>, so the lifecycle's cancel finds them.
/// </summary>
internal static class LifecycleTestRecord
{
    public const string AttachableRecordType = "test-record-files";
    public const string ViewOwn = "test-records.view-own";
    public const string Queue = "test-records.queue";

    public static TransitionCatalog Catalog { get; } = new(
        TestRecordWorkflow.RecordType,
        TestRecordWorkflow.Statuses,
        [.. TestRecordWorkflow.Rules.Select(rule => rule.ActionKey == TestRecordActions.Withdraw ? rule with { ActionKey = RecordLifecycleActions.Cancel } : rule)]);

    public static RecordTypeRegistration Registration { get; } = new(
        TestRecordWorkflow.RecordType,
        TestRecordWorkflow.NumberPrefix,
        QueuePermissionKey: Queue,
        ViewOwnPermissionKey: ViewOwn,
        AttachableRecordType: AttachableRecordType);

    public sealed class Definition : IRecordWorkflowDefinition
    {
        public string RecordType => TestRecordWorkflow.RecordType;

        public TransitionCatalog Catalog => LifecycleTestRecord.Catalog;
    }
}

/// <summary>
/// Every Records service wired by hand over the fakes, with four users: a requester and a staff member of organization
/// 100, a staff member of organization 200, and an oversight admin. The clock starts at 2026-11-06 08:00 UTC.
/// </summary>
internal sealed class RecordsHarness
{
    public const long OwnOrganization = 100;
    public const long OtherOrganization = 200;

    public RecordsHarness()
    {
        Requester = Users.Add(userId: 10, "Requester One", OwnOrganization)
            .Holds(TestRecordPermissionKeys.Create).Holds(LifecycleTestRecord.ViewOwn);
        Staff = Users.Add(userId: 20, "Staff One", OwnOrganization)
            .Holds(LifecycleTestRecord.Queue, PermissionScope.OwnOrganization)
            .Holds(TestRecordPermissionKeys.Review, PermissionScope.OwnOrganization)
            .Holds(TestRecordPermissionKeys.Decide, PermissionScope.OwnOrganization);
        OtherStaff = Users.Add(userId: 30, "Staff Two", OtherOrganization)
            .Holds(LifecycleTestRecord.Queue, PermissionScope.OwnOrganization)
            .Holds(TestRecordPermissionKeys.Review, PermissionScope.OwnOrganization);
        Admin = Users.Add(userId: 40, "Admin One", organizationId: null).Holds(RecordsPermissionKeys.OversightView);
        Users.SignIn(Requester);
        Types = new RecordTypes([new FakeRegistrations(LifecycleTestRecord.Registration)], new RecordWorkflowCatalogs([new LifecycleTestRecord.Definition()]));
        var callers = new RecordCallers(Users, Users);
        Standings = new RecordStandings(Users);
        Loader = new RecordLoader(Types, Store, callers, Standings);
        Effects = new TransitionEffects([Observer], [Assignments], Notifications, NullLogger<TransitionEffects>.Instance);
        var commits = new RecordCommits(Store, Clock, Effects, NullLogger<RecordCommits>.Instance);
        Submissions = new RecordSubmissions(Loader, commits, [Validator], [Interlock], Numbers);
        Transitions = new RecordTransitions(Loader, commits, Submissions);
        Lifecycle = new RecordLifecycle(new RecordDrafts(Types, callers, Standings, Store, Clock), Submissions, Transitions);
        Names = new RecordUserNames(new FakeAccounts(Users));
        Details = new GetRecordDetail(Loader, Store, Names, new RecordEnrichment([Assignments], [Sla]));
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero));

    public FakeUsers Users { get; } = new();

    public FakeRecordStore Store { get; } = new();

    public FakeNotifications Notifications { get; } = new();

    public FakeRequiredAttachments Interlock { get; } = new();

    public FakeNumbers Numbers { get; } = new();

    public FakeSubmitValidator Validator { get; } = new(TestRecordWorkflow.RecordType);

    public FakeObserver Observer { get; } = new();

    public FakeAssignments Assignments { get; } = new();

    public FakeSla Sla { get; } = new();

    public TestUser Requester { get; }

    public TestUser Staff { get; }

    public TestUser OtherStaff { get; }

    public TestUser Admin { get; }

    public RecordTypes Types { get; }

    public RecordStandings Standings { get; }

    public RecordLoader Loader { get; }

    public TransitionEffects Effects { get; }

    public RecordSubmissions Submissions { get; }

    public RecordTransitions Transitions { get; }

    public RecordLifecycle Lifecycle { get; }

    public RecordUserNames Names { get; }

    public GetRecordDetail Details { get; }

    public void SignIn(TestUser? user) => Users.SignIn(user);

    /// <summary>A draft created by the requester (signs the requester in).</summary>
    public async Task<RecordRef> DraftAsync()
    {
        SignIn(Requester);
        Clock.Advance(TimeSpan.FromMinutes(1));
        return await Lifecycle.CreateDraftAsync(TestRecordWorkflow.RecordType, CancellationToken.None);
    }

    /// <summary>A record the requester submitted.</summary>
    public async Task<RecordRef> SubmittedAsync()
    {
        var record = await DraftAsync();
        Assert.IsType<SubmitResult.Submitted>(await Lifecycle.SubmitAsync(record, CancellationToken.None));
        return record;
    }

    /// <summary>A record staff took into review.</summary>
    public async Task<RecordRef> InReviewAsync()
    {
        var record = await SubmittedAsync();
        await AsAsync(Staff, record, TestRecordActions.Take);
        return record;
    }

    /// <summary>Takes <paramref name="action"/> as <paramref name="user"/>, asserting success.</summary>
    public async Task<int> AsAsync(TestUser user, RecordRef record, string action, string? reason = null)
    {
        SignIn(user);
        Clock.Advance(TimeSpan.FromMinutes(1));
        var result = await Lifecycle.TransitionAsync(record, action, reason, CancellationToken.None);
        return Assert.IsType<TransitionResult.Transitioned>(result).NewStatus;
    }

    /// <summary>Takes <paramref name="action"/> as <paramref name="user"/> and returns the refusal.</summary>
    public async Task<Starter.Application.Results.Refusal> RefusedAsync(TestUser user, RecordRef record, string action, string? reason = null)
    {
        SignIn(user);
        var result = await Lifecycle.TransitionAsync(record, action, reason, CancellationToken.None);
        return Assert.IsType<TransitionResult.Refused>(result).Refusal;
    }

    public Record RecordOf(RecordRef record) => Store.Records.Single(candidate => candidate.Id == record.Id);
}


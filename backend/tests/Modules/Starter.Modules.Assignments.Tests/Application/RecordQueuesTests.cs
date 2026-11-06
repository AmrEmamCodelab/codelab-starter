using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Application.Workflow;
using Starter.Modules.Assignments.Application;
using Starter.Modules.Assignments.Application.Queues;
using Starter.Modules.Assignments.Tests.Fakes;

namespace Starter.Modules.Assignments.Tests.Application;

public sealed class RecordQueuesTests
{
    private readonly TestWorld _world = new();

    [Theory]
    [InlineData("other-record")]
    [InlineData("Not A Key")]
    [InlineData("")]
    public async Task An_unknown_or_malformed_record_type_has_no_queue(string recordType)
    {
        var opened = await _world.Queues.OpenAsync(recordType, CancellationToken.None);

        AssertRefused(opened, RefusalCategory.NotFound, AssignmentsErrorCodes.RecordTypeUnknown);
    }

    [Fact]
    public async Task A_registered_record_type_without_a_workflow_has_no_queue()
    {
        var queues = new RecordQueues([TestRecordType.Instance], new RecordWorkflowCatalogs([]), _world.Scope, _world.Directory);

        var opened = await queues.OpenAsync(TestRecordType.Key, CancellationToken.None);

        AssertRefused(opened, RefusalCategory.NotFound, AssignmentsErrorCodes.RecordTypeUnknown);
    }

    [Fact]
    public async Task A_caller_without_the_queue_permission_is_forbidden()
    {
        _world.Scope.Decisions.Clear();

        var opened = await _world.Queues.OpenAsync(TestRecordType.Key, CancellationToken.None);

        AssertRefused(opened, RefusalCategory.Forbidden, AssignmentsErrorCodes.QueueForbidden);
    }

    [Fact]
    public async Task The_queue_lists_neither_the_initial_nor_a_terminal_status()
    {
        var queue = (await _world.Queues.OpenAsync(TestRecordType.Key, CancellationToken.None)).Value;

        Assert.Equal([TestRecordStatuses.Submitted, TestRecordStatuses.InReview], queue.OpenStatuses);
        Assert.Equal("in-review", queue.StatusKey(TestRecordStatuses.InReview));
        Assert.Equal("42", queue.StatusKey(42));
    }

    [Fact]
    public async Task The_status_filter_accepts_only_open_statuses()
    {
        var queue = (await _world.Queues.OpenAsync(TestRecordType.Key, CancellationToken.None)).Value;

        Assert.Equal(queue.OpenStatuses, queue.StatusFilter(status: null));
        Assert.Equal([TestRecordStatuses.InReview], queue.StatusFilter(TestRecordStatuses.InReview));
        Assert.Null(queue.StatusFilter(TestRecordStatuses.Draft));
        Assert.Null(queue.StatusFilter(TestRecordStatuses.Closed));
        Assert.Null(queue.StatusFilter(42));
    }

    [Fact]
    public async Task A_record_of_another_organization_is_not_found_for_an_own_organization_holder()
    {
        _world.Scope.Decisions[TestRecordType.QueuePermission] = TestWorld.OwnOrganizationOnly;
        _world.AddRecord(id: 1, organizationId: TestWorld.OtherOrganization);
        _world.AddRecord(id: 2, organizationId: TestWorld.OwnOrganization);

        var foreign = await _world.Queues.FindAsync(TestWorld.Item(1), CancellationToken.None);
        var own = await _world.Queues.FindAsync(TestWorld.Item(2), CancellationToken.None);

        AssertRefused(foreign, RefusalCategory.NotFound, RecordsErrorCodes.NotFound);
        Assert.True(own.IsSuccess);
    }

    [Fact]
    public async Task A_missing_record_or_one_of_another_type_is_not_found()
    {
        _world.Directory.Records.Add(_world.AddRecord(id: 3) with { Id = 4, RecordType = "other-record" });

        AssertRefused(await _world.Queues.FindAsync(TestWorld.Item(99), CancellationToken.None), RefusalCategory.NotFound, RecordsErrorCodes.NotFound);
        AssertRefused(await _world.Queues.FindAsync(TestWorld.Item(4), CancellationToken.None), RefusalCategory.NotFound, RecordsErrorCodes.NotFound);
    }

    [Theory]
    [InlineData(TestRecordStatuses.Draft)]
    [InlineData(TestRecordStatuses.Closed)]
    public async Task A_draft_or_closed_record_is_not_assignable(int status)
    {
        _world.AddRecord(id: 1, status: status);

        var found = await _world.Queues.FindOpenAsync(TestWorld.Item(1), CancellationToken.None);

        AssertRefused(found, RefusalCategory.Conflict, AssignmentsErrorCodes.NotAssignable);
    }

    [Fact]
    public async Task An_open_record_is_found_with_its_reference()
    {
        _world.AddRecord(id: 1, status: TestRecordStatuses.InReview);

        var found = await _world.Queues.FindOpenAsync(TestWorld.Item(1), CancellationToken.None);

        Assert.Equal(1, found.Value.Ref.Id);
        Assert.Equal(TestRecordType.Key, found.Value.Ref.RecordType);
    }

    [Fact]
    public void A_global_decision_reaches_a_record_without_an_organization()
    {
        Assert.True(TestWorld.Global.Reaches(organizationId: null));
        Assert.False(new ScopeDecision(Allowed: true, Scope: PermissionScope.OwnOrganization, OrganizationId: null).Reaches(TestWorld.OwnOrganization));
    }

    internal static void AssertRefused<T>(Outcome<T> outcome, RefusalCategory category, string code)
    {
        Assert.False(outcome.IsSuccess);
        Assert.Equal(category, outcome.Refusal.Category);
        Assert.Equal(code, outcome.Refusal.Code);
    }
}

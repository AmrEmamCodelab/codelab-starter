using Codelab.Security.PublicIds;
using Starter.Application.Authorization;
using Starter.Application.Results;
using Starter.Modules.Assignments.Application;
using Starter.Modules.Assignments.Application.Commands;
using Starter.Modules.Assignments.Domain;
using Starter.Modules.Assignments.Tests.Fakes;
using static Starter.Modules.Assignments.Tests.Application.RecordQueuesTests;

namespace Starter.Modules.Assignments.Tests.Application;

public sealed class AssignRecordTests
{
    private readonly TestWorld _world = new();

    public AssignRecordTests()
    {
        _world.AddRecord(id: 1, organizationId: TestWorld.OwnOrganization);
    }

    [Fact]
    public async Task A_supervisor_assigns_the_record_to_an_eligible_user()
    {
        MakeEligible(TestWorld.OtherStaffUserId, TestWorld.Global);

        var assigned = await _world.Assign.ExecuteAsync(TestWorld.Item(1), Command(TestWorld.OtherStaffUserId), CancellationToken.None);

        Assert.True(assigned.IsSuccess);
        var row = Assert.Single(_world.Store.Rows);
        Assert.Equal(TestWorld.OtherStaffUserId, row.AssigneeUserId);
        Assert.Equal(TestWorld.StaffUserId, row.AssignedByUserId);
        Assert.True(row.IsOpen);
    }

    [Fact]
    public async Task Assigning_to_a_user_without_the_queue_permission_is_refused()
    {
        var assigned = await _world.Assign.ExecuteAsync(TestWorld.Item(1), Command(TestWorld.OtherStaffUserId), CancellationToken.None);

        AssertRefused(assigned, RefusalCategory.Invalid, AssignmentsErrorCodes.AssigneeNotEligible);
        Assert.Empty(_world.Store.Rows);
    }

    [Fact]
    public async Task Assigning_to_a_user_whose_scope_misses_the_records_organization_is_refused()
    {
        MakeEligible(TestWorld.OtherStaffUserId, new ScopeDecision(Allowed: true, Scope: PermissionScope.OwnOrganization, OrganizationId: TestWorld.OtherOrganization));

        var assigned = await _world.Assign.ExecuteAsync(TestWorld.Item(1), Command(TestWorld.OtherStaffUserId), CancellationToken.None);

        AssertRefused(assigned, RefusalCategory.Invalid, AssignmentsErrorCodes.AssigneeNotEligible);
    }

    [Fact]
    public async Task Assigning_a_held_record_moves_it_and_keeps_the_previous_row_as_history()
    {
        var previous = _world.SeedOpen(recordId: 1, assigneeUserId: TestWorld.StaffUserId);
        MakeEligible(TestWorld.OtherStaffUserId, TestWorld.OwnOrganizationOnly);

        var assigned = await _world.Assign.ExecuteAsync(TestWorld.Item(1), Command(TestWorld.OtherStaffUserId), CancellationToken.None);

        Assert.True(assigned.IsSuccess);
        Assert.Equal(AssignmentReleaseReason.Reassigned, previous.ReleaseReason);
        Assert.Equal(TestWorld.StaffUserId, previous.ReleasedByUserId);
        Assert.Equal(TestWorld.OtherStaffUserId, Assert.Single(_world.Store.Rows, row => row.IsOpen).AssigneeUserId);
    }

    [Fact]
    public async Task Assigning_the_current_holder_again_changes_nothing()
    {
        var current = _world.SeedOpen(recordId: 1, assigneeUserId: TestWorld.OtherStaffUserId);
        MakeEligible(TestWorld.OtherStaffUserId, TestWorld.Global);

        var assigned = await _world.Assign.ExecuteAsync(TestWorld.Item(1), Command(TestWorld.OtherStaffUserId), CancellationToken.None);

        Assert.True(assigned.IsSuccess);
        Assert.True(current.IsOpen);
        Assert.Single(_world.Store.Rows);
        Assert.Equal(0, _world.Store.Saves);
    }

    [Fact]
    public async Task An_assignment_that_loses_the_race_to_a_claim_is_refused()
    {
        MakeEligible(TestWorld.OtherStaffUserId, TestWorld.Global);
        _world.Store.ConcurrentClaimOnNextSave = TestWorld.OpenRow(recordId: 1, assigneeUserId: 99);

        var assigned = await _world.Assign.ExecuteAsync(TestWorld.Item(1), Command(TestWorld.OtherStaffUserId), CancellationToken.None);

        AssertRefused(assigned, RefusalCategory.Conflict, AssignmentsErrorCodes.AlreadyClaimed);
    }

    [Fact]
    public async Task A_missing_user_id_is_a_validation_refusal()
    {
        var assigned = await _world.Assign.ExecuteAsync(TestWorld.Item(1), new AssignRecordCommand(default), CancellationToken.None);

        AssertRefused(assigned, RefusalCategory.Invalid, PlatformErrorCodes.Validation);
        Assert.Contains("userId", assigned.Refusal!.FieldErrors.Keys);
    }

    [Fact]
    public async Task A_closed_record_cannot_be_assigned()
    {
        _world.AddRecord(id: 2, status: TestRecordStatuses.Closed);
        MakeEligible(TestWorld.OtherStaffUserId, TestWorld.Global);

        var assigned = await _world.Assign.ExecuteAsync(TestWorld.Item(2), Command(TestWorld.OtherStaffUserId), CancellationToken.None);

        AssertRefused(assigned, RefusalCategory.Conflict, AssignmentsErrorCodes.NotAssignable);
    }

    private void MakeEligible(long userId, ScopeDecision decision) =>
        _world.Eligibility.Decisions[(userId, TestRecordType.QueuePermission)] = decision;

    private static AssignRecordCommand Command(long userId) => new(PublicId<AssigneeUserIds>.From(userId));
}

using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Modules.Assignments.Application;
using Starter.Modules.Assignments.Domain;
using Starter.Modules.Assignments.Tests.Fakes;
using static Starter.Modules.Assignments.Tests.Application.RecordQueuesTests;

namespace Starter.Modules.Assignments.Tests.Application;

public sealed class ClaimRecordTests
{
    private readonly TestWorld _world = new();

    [Fact]
    public async Task A_claim_opens_a_row_held_and_made_by_the_caller_now()
    {
        _world.AddRecord(id: 1);

        var claimed = await _world.Claim.ExecuteAsync(TestWorld.Item(1), CancellationToken.None);

        Assert.True(claimed.IsSuccess);
        var row = Assert.Single(_world.Store.Rows);
        Assert.True(row.IsOpen);
        Assert.Equal(1, row.RecordId);
        Assert.Equal(TestWorld.StaffUserId, row.AssigneeUserId);
        Assert.Equal(TestWorld.StaffUserId, row.AssignedByUserId);
        Assert.Equal(TestWorld.Now, row.AssignedAt);
    }

    [Fact]
    public async Task Claiming_twice_is_refused_as_already_claimed()
    {
        _world.AddRecord(id: 1);
        await _world.Claim.ExecuteAsync(TestWorld.Item(1), CancellationToken.None);

        var again = await _world.Claim.ExecuteAsync(TestWorld.Item(1), CancellationToken.None);

        AssertRefused(again, RefusalCategory.Conflict, AssignmentsErrorCodes.AlreadyClaimed);
        Assert.Single(_world.Store.Rows);
    }

    [Fact]
    public async Task A_record_another_user_holds_is_refused_as_already_claimed()
    {
        _world.AddRecord(id: 1);
        _world.SeedOpen(recordId: 1, assigneeUserId: TestWorld.OtherStaffUserId);

        var claimed = await _world.Claim.ExecuteAsync(TestWorld.Item(1), CancellationToken.None);

        AssertRefused(claimed, RefusalCategory.Conflict, AssignmentsErrorCodes.AlreadyClaimed);
    }

    [Fact]
    public async Task A_claim_that_loses_the_race_to_a_concurrent_claim_is_refused()
    {
        _world.AddRecord(id: 1);
        _world.Store.ConcurrentClaimOnNextSave = TestWorld.OpenRow(recordId: 1, assigneeUserId: TestWorld.OtherStaffUserId);

        var claimed = await _world.Claim.ExecuteAsync(TestWorld.Item(1), CancellationToken.None);

        AssertRefused(claimed, RefusalCategory.Conflict, AssignmentsErrorCodes.AlreadyClaimed);
        Assert.Equal(TestWorld.OtherStaffUserId, Assert.Single(_world.Store.Rows).AssigneeUserId);
    }

    [Fact]
    public async Task An_anonymous_or_unknown_caller_is_forbidden()
    {
        _world.AddRecord(id: 1);
        _world.Users.SignOut();
        var anonymous = await _world.Claim.ExecuteAsync(TestWorld.Item(1), CancellationToken.None);
        _world.Users.SignIn(Guid.NewGuid());
        var unknown = await _world.Claim.ExecuteAsync(TestWorld.Item(1), CancellationToken.None);
        _world.Users.SignIn(_world.Users.Add(userId: 30, displayName: "Inactive", isActive: false));
        var inactive = await _world.Claim.ExecuteAsync(TestWorld.Item(1), CancellationToken.None);

        AssertRefused(anonymous, RefusalCategory.Forbidden, AssignmentsErrorCodes.ActorUnknown);
        AssertRefused(unknown, RefusalCategory.Forbidden, AssignmentsErrorCodes.ActorUnknown);
        AssertRefused(inactive, RefusalCategory.Forbidden, AssignmentsErrorCodes.ActorUnknown);
        Assert.Empty(_world.Store.Rows);
    }

    [Fact]
    public async Task A_record_outside_the_callers_organization_scope_is_not_found()
    {
        _world.Scope.Decisions[TestRecordType.QueuePermission] = TestWorld.OwnOrganizationOnly;
        _world.AddRecord(id: 1, organizationId: TestWorld.OtherOrganization);

        var claimed = await _world.Claim.ExecuteAsync(TestWorld.Item(1), CancellationToken.None);

        AssertRefused(claimed, RefusalCategory.NotFound, RecordsErrorCodes.NotFound);
        Assert.Empty(_world.Store.Rows);
    }

    [Fact]
    public async Task A_closed_record_cannot_be_claimed()
    {
        _world.AddRecord(id: 1, status: TestRecordStatuses.Closed);

        var claimed = await _world.Claim.ExecuteAsync(TestWorld.Item(1), CancellationToken.None);

        AssertRefused(claimed, RefusalCategory.Conflict, AssignmentsErrorCodes.NotAssignable);
    }
}

public sealed class ReleaseRecordTests
{
    private readonly TestWorld _world = new();

    [Fact]
    public async Task The_assignee_releases_the_record_back_to_the_queue()
    {
        _world.AddRecord(id: 1);
        var row = _world.SeedOpen(recordId: 1, assigneeUserId: TestWorld.StaffUserId);

        var released = await _world.Release.ExecuteAsync(TestWorld.Item(1), CancellationToken.None);

        Assert.True(released.IsSuccess);
        Assert.False(row.IsOpen);
        Assert.Equal(AssignmentReleaseReason.Released, row.ReleaseReason);
        Assert.Equal(TestWorld.StaffUserId, row.ReleasedByUserId);
        Assert.Equal(TestWorld.Now, row.ReleasedAt);
        Assert.Equal(1, _world.Store.Saves);
    }

    [Fact]
    public async Task A_release_by_someone_other_than_the_assignee_is_refused()
    {
        _world.AddRecord(id: 1);
        var row = _world.SeedOpen(recordId: 1, assigneeUserId: TestWorld.OtherStaffUserId);

        var released = await _world.Release.ExecuteAsync(TestWorld.Item(1), CancellationToken.None);

        AssertRefused(released, RefusalCategory.Forbidden, AssignmentsErrorCodes.NotAssignee);
        Assert.True(row.IsOpen);
        Assert.Equal(0, _world.Store.Saves);
    }

    [Fact]
    public async Task A_release_of_an_unassigned_record_is_refused()
    {
        _world.AddRecord(id: 1);

        var released = await _world.Release.ExecuteAsync(TestWorld.Item(1), CancellationToken.None);

        AssertRefused(released, RefusalCategory.Forbidden, AssignmentsErrorCodes.NotAssignee);
    }

    [Fact]
    public async Task A_released_record_can_be_claimed_again_and_keeps_its_history()
    {
        _world.AddRecord(id: 1);
        await _world.Claim.ExecuteAsync(TestWorld.Item(1), CancellationToken.None);
        await _world.Release.ExecuteAsync(TestWorld.Item(1), CancellationToken.None);
        _world.Users.SignIn(_world.OtherStaffSubject);

        var claimed = await _world.Claim.ExecuteAsync(TestWorld.Item(1), CancellationToken.None);

        Assert.True(claimed.IsSuccess);
        Assert.Equal(2, _world.Store.Rows.Count);
        Assert.Equal(TestWorld.OtherStaffUserId, Assert.Single(_world.Store.Rows, row => row.IsOpen).AssigneeUserId);
    }
}

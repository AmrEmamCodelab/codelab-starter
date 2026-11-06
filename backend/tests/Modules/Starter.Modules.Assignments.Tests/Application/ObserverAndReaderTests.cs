using Starter.Application.Records;
using Starter.Modules.Assignments.Application.Queues;
using Starter.Modules.Assignments.Domain;
using Starter.Modules.Assignments.Tests.Fakes;

namespace Starter.Modules.Assignments.Tests.Application;

public sealed class CloseAssignmentOnTerminalTests
{
    private const long ActorUserId = 40;
    private static readonly DateTimeOffset TransitionAt = TestWorld.Now.AddMinutes(5);

    private readonly TestWorld _world = new();

    [Fact]
    public async Task A_terminal_transition_closes_the_open_assignment_at_the_transition_time()
    {
        var row = _world.SeedOpen(recordId: 1, assigneeUserId: TestWorld.StaffUserId);

        await _world.Observer.OnTransitionedAsync(Transition(recordId: 1, toTerminal: true), CancellationToken.None);

        Assert.False(row.IsOpen);
        Assert.Equal(AssignmentReleaseReason.RecordClosed, row.ReleaseReason);
        Assert.Equal(ActorUserId, row.ReleasedByUserId);
        Assert.Equal(TransitionAt, row.ReleasedAt);
    }

    [Fact]
    public async Task A_non_terminal_transition_keeps_the_assignee()
    {
        var row = _world.SeedOpen(recordId: 1, assigneeUserId: TestWorld.StaffUserId);

        await _world.Observer.OnTransitionedAsync(Transition(recordId: 1, toTerminal: false), CancellationToken.None);

        Assert.True(row.IsOpen);
        Assert.Equal(0, _world.Store.Saves);
    }

    [Fact]
    public async Task The_close_is_idempotent_and_ignores_unassigned_records()
    {
        _world.SeedOpen(recordId: 1, assigneeUserId: TestWorld.StaffUserId);

        await _world.Observer.OnTransitionedAsync(Transition(recordId: 1, toTerminal: true), CancellationToken.None);
        await _world.Observer.OnTransitionedAsync(Transition(recordId: 1, toTerminal: true), CancellationToken.None);
        await _world.Observer.OnTransitionedAsync(Transition(recordId: 2, toTerminal: true), CancellationToken.None);

        Assert.Equal(1, _world.Store.Saves);
    }

    private static RecordTransitioned Transition(long recordId, bool toTerminal) =>
        new(
            RecordId: recordId,
            RecordType: TestRecordType.Key,
            FromStatus: TestRecordStatuses.InReview,
            ToStatus: toTerminal ? TestRecordStatuses.Closed : TestRecordStatuses.Submitted,
            ActionKey: "close",
            ActorUserId: ActorUserId,
            ToTerminal: toTerminal,
            At: TransitionAt);
}

public sealed class RecordAssignmentReaderTests
{
    private readonly TestWorld _world = new();

    [Fact]
    public async Task The_reader_returns_the_open_assignment_with_the_assignees_name()
    {
        var row = _world.SeedOpen(recordId: 1, assigneeUserId: TestWorld.OtherStaffUserId);
        var closed = _world.SeedOpen(recordId: 2, assigneeUserId: TestWorld.StaffUserId);
        closed.Release(new AssignmentEnd(AssignmentReleaseReason.Released, ReleasedByUserId: TestWorld.StaffUserId, At: TestWorld.Now));

        var current = await _world.Reader.CurrentAsync([1, 2, 3], CancellationToken.None);

        var snapshot = Assert.Single(current).Value;
        Assert.Equal(TestWorld.OtherStaffUserId, snapshot.AssigneeUserId);
        Assert.Equal(TestWorld.OtherStaffName, snapshot.AssigneeDisplayName);
        Assert.Equal(row.AssignedAt, snapshot.AssignedAt);
    }

    [Fact]
    public async Task No_ids_read_nothing()
    {
        Assert.Empty(await _world.Reader.CurrentAsync([], CancellationToken.None));
    }

    [Fact]
    public async Task An_unknown_user_has_the_empty_display_name()
    {
        var names = await _world.Names.ForAsync([TestWorld.StaffUserId, 999, TestWorld.StaffUserId], CancellationToken.None);

        Assert.Equal(2, names.Count);
        Assert.Equal(TestWorld.StaffName, names[TestWorld.StaffUserId]);
        Assert.Equal(UserDisplayNames.Unknown, names[999]);
    }
}

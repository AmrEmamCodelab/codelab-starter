using Starter.Domain.Records;
using Starter.Modules.Assignments.Domain;
using Starter.Modules.Assignments.Tests.Fakes;

namespace Starter.Modules.Assignments.Tests.Domain;

public sealed class RecordAssignmentTests
{
    private static readonly RecordRef Record = new(5, TestRecordType.Key);

    [Fact]
    public void Open_starts_an_open_row_for_the_record_and_the_assignee()
    {
        var row = RecordAssignment.Open(new AssignmentStart(Record, AssigneeUserId: 10, AssignedByUserId: 12, At: TestWorld.Now));

        Assert.True(row.IsOpen);
        Assert.Equal(TestRecordType.Key, row.RecordType);
        Assert.Equal(5, row.RecordId);
        Assert.Equal(10, row.AssigneeUserId);
        Assert.Equal(12, row.AssignedByUserId);
        Assert.Equal(TestWorld.Now, row.AssignedAt);
        Assert.Null(row.ReleasedAt);
        Assert.Null(row.ReleaseReason);
        Assert.Null(row.ReleasedByUserId);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Open_refuses_a_user_id_below_one(long assignee, long assignedBy)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecordAssignment.Open(new AssignmentStart(Record, AssigneeUserId: assignee, AssignedByUserId: assignedBy, At: TestWorld.Now)));
    }

    [Fact]
    public void Release_closes_the_row_with_the_reason_the_actor_and_the_time()
    {
        var row = TestWorld.OpenRow(recordId: 5, assigneeUserId: 10);

        row.Release(new AssignmentEnd(AssignmentReleaseReason.Reassigned, ReleasedByUserId: 12, At: TestWorld.Now));

        Assert.False(row.IsOpen);
        Assert.Equal(TestWorld.Now, row.ReleasedAt);
        Assert.Equal(AssignmentReleaseReason.Reassigned, row.ReleaseReason);
        Assert.Equal(12, row.ReleasedByUserId);
    }

    [Fact]
    public void A_closed_row_is_never_released_again()
    {
        var row = TestWorld.OpenRow(recordId: 5, assigneeUserId: 10);
        row.Release(new AssignmentEnd(AssignmentReleaseReason.Released, ReleasedByUserId: 10, At: TestWorld.Now));

        Assert.Throws<InvalidOperationException>(() =>
            row.Release(new AssignmentEnd(AssignmentReleaseReason.Released, ReleasedByUserId: 10, At: TestWorld.Now)));
    }

    [Fact]
    public void An_end_before_the_start_is_recorded_at_the_start()
    {
        var row = TestWorld.OpenRow(recordId: 5, assigneeUserId: 10);

        row.Release(new AssignmentEnd(AssignmentReleaseReason.RecordClosed, ReleasedByUserId: 10, At: row.AssignedAt.AddMinutes(-5)));

        Assert.Equal(row.AssignedAt, row.ReleasedAt);
    }

    [Fact]
    public void An_undefined_reason_is_refused()
    {
        var row = TestWorld.OpenRow(recordId: 5, assigneeUserId: 10);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            row.Release(new AssignmentEnd((AssignmentReleaseReason)99, ReleasedByUserId: 10, At: TestWorld.Now)));
        Assert.True(row.IsOpen);
    }

    [Fact]
    public void Enum_members_have_their_stored_values()
    {
        Assert.Equal(1, (int)AssignmentReleaseReason.Released);
        Assert.Equal(2, (int)AssignmentReleaseReason.Reassigned);
        Assert.Equal(3, (int)AssignmentReleaseReason.RecordClosed);
        Assert.Equal(1, (int)QueueView.Unassigned);
        Assert.Equal(2, (int)QueueView.Mine);
        Assert.Equal(3, (int)QueueView.All);
    }
}

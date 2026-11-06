using Starter.Domain.Auditing;
using Starter.Domain.Workflow;
using Starter.Modules.Records.Domain;

namespace Starter.Modules.Records.Tests.Domain;

public sealed class RecordTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 6, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_draft_starts_in_the_initial_status_with_no_number()
    {
        var record = Draft();

        Assert.Equal("test-record", record.RecordType);
        Assert.Equal(1, record.Status);
        Assert.Equal(10, record.OwnerUserId);
        Assert.Equal(100, record.OrganizationId);
        Assert.Null(record.Number);
        Assert.Null(record.SubmittedAt);
        Assert.Equal(At, record.CreatedAt);
        Assert.Equal(At, record.UpdatedAt);
        Assert.True(record.IsOwnedBy(10));
        Assert.False(record.IsOwnedBy(11));
    }

    [Theory]
    [InlineData("Not-Kebab", 10)]
    [InlineData("test-record", 0)]
    public void A_draft_refuses_a_malformed_type_or_owner(string recordType, long owner)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            Record.StartDraft(new RecordDraft(recordType, InitialStatus: 1, OwnerUserId: owner, OrganizationId: null, At: At)));
    }

    [Fact]
    public void The_first_submit_sets_the_number_and_time_and_a_resubmit_keeps_both()
    {
        var record = Draft();

        record.MarkSubmitted("TR-2026-001", At.AddHours(1));
        record.MarkSubmitted("TR-2026-001", At.AddHours(2));

        Assert.Equal("TR-2026-001", record.Number);
        Assert.Equal(At.AddHours(1), record.SubmittedAt);
    }

    [Fact]
    public void A_second_different_number_is_refused()
    {
        var record = Draft();
        record.MarkSubmitted("TR-2026-001", At);

        Assert.Throws<InvalidOperationException>(() => record.MarkSubmitted("TR-2026-002", At));
    }

    [Fact]
    public void A_number_longer_than_the_column_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Draft().MarkSubmitted(new string('T', RecordColumnLimits.NumberMaxLength + 1), At));
    }

    [Fact]
    public void Moving_changes_the_status_and_the_update_time_only()
    {
        var record = Draft();

        record.MoveTo(status: 3, At.AddDays(1));

        Assert.Equal(3, record.Status);
        Assert.Equal(At.AddDays(1), record.UpdatedAt);
        Assert.Equal(At, record.CreatedAt);
    }

    [Fact]
    public void A_timeline_entry_keeps_its_facts_and_side()
    {
        var entry = new RecordHistory(
            new RecordHistoryFacts(RecordId: 5, Action: "return", FromStatus: 3, ToStatus: 4, Reason: "Missing page", ActorUserId: 20, ActorName: "Staff One", At: At),
            TransitionSide.Staff);

        Assert.Equal(TransitionSide.Staff, entry.Side);
        Assert.Equal(5, entry.RecordId);
        Assert.Equal("return", entry.Action);
        Assert.Equal(3, entry.FromStatus);
        Assert.Equal(4, entry.ToStatus);
        Assert.Equal("Missing page", entry.Reason);
    }

    [Fact]
    public void A_timeline_entry_refuses_a_blank_actor()
    {
        Assert.ThrowsAny<ArgumentException>(() => new RecordHistory(
            new RecordHistoryFacts(RecordId: 5, Action: "return", FromStatus: 3, ToStatus: 4, Reason: null, ActorUserId: 20, ActorName: " ", At: At),
            TransitionSide.Staff));
    }

    private static Record Draft() =>
        Record.StartDraft(new RecordDraft("test-record", InitialStatus: 1, OwnerUserId: 10, OrganizationId: 100, At: At));
}

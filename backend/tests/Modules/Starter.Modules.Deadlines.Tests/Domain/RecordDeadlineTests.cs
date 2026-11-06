using Starter.Application.Records;
using Starter.Modules.Deadlines.Domain;
using Starter.Modules.Deadlines.Tests.Fakes;

namespace Starter.Modules.Deadlines.Tests.Domain;

public sealed class RecordDeadlineTests
{
    private static readonly DateTimeOffset Due = TestRecord.Start + TestRecord.ReviewSla;
    private static readonly DateTimeOffset DueSoon = Due - TestRecord.ReviewDueSoon;

    [Fact]
    public void Opening_freezes_the_due_and_due_soon_times_of_the_definition()
    {
        var deadline = Open();

        Assert.Equal(7, deadline.RecordId);
        Assert.Equal(TestRecord.Type, deadline.RecordType);
        Assert.Equal(TestRecord.UnderReview, deadline.Status);
        Assert.Equal(TestRecord.Start, deadline.StartedAt);
        Assert.Equal(Due, deadline.DueAt);
        Assert.Equal(DueSoon, deadline.DueSoonAt);
        Assert.True(deadline.IsOpen);
        Assert.Null(deadline.BreachedAt);
        Assert.Null(deadline.EscalatedAt);
    }

    [Fact]
    public void A_record_id_below_one_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RecordDeadline.Open(0, TestRecord.ReviewDefinition(), TestRecord.Start));

    [Fact]
    public void Without_a_due_soon_window_due_soon_is_the_deadline()
    {
        var definition = new SlaDefinition(TestRecord.Type, TestRecord.UnderReview, TestRecord.ReviewSla, TimeSpan.Zero, TestRecord.EscalationTemplate);

        var deadline = RecordDeadline.Open(7, definition, TestRecord.Start);

        Assert.Equal(deadline.DueAt, deadline.DueSoonAt);
        Assert.Equal(SlaState.OnTrack, deadline.BadgeAt(deadline.DueAt - TimeSpan.FromTicks(1)).State);
    }

    [Fact]
    public void The_badge_is_on_track_until_the_due_soon_threshold()
    {
        var deadline = Open();

        Assert.Equal(new SlaBadge(SlaState.OnTrack, Due), deadline.BadgeAt(TestRecord.Start));
        Assert.Equal(SlaState.OnTrack, deadline.BadgeAt(DueSoon - TimeSpan.FromTicks(1)).State);
    }

    [Fact]
    public void The_badge_is_due_soon_from_the_threshold_until_just_before_the_deadline()
    {
        var deadline = Open();

        Assert.Equal(new SlaBadge(SlaState.DueSoon, Due), deadline.BadgeAt(DueSoon));
        Assert.Equal(SlaState.DueSoon, deadline.BadgeAt(Due - TimeSpan.FromTicks(1)).State);
    }

    [Fact]
    public void The_badge_is_breached_exactly_at_the_deadline_and_after_it()
    {
        var deadline = Open();

        Assert.Equal(new SlaBadge(SlaState.Breached, Due), deadline.BadgeAt(Due));
        Assert.Equal(SlaState.Breached, deadline.BadgeAt(Due + TimeSpan.FromDays(10)).State);
    }

    [Fact]
    public void The_badge_agrees_with_the_kernel_rule_of_the_definition()
    {
        var definition = TestRecord.ReviewDefinition();
        var deadline = Open();

        foreach (var offset in new[] { TimeSpan.Zero, TimeSpan.FromDays(2), TimeSpan.FromDays(3), TimeSpan.FromDays(4) })
        {
            Assert.Equal(definition.BadgeAt(TestRecord.Start, TestRecord.Start + offset), deadline.BadgeAt(TestRecord.Start + offset));
        }
    }

    [Fact]
    public void A_breach_is_marked_exactly_at_the_deadline_and_only_once()
    {
        var deadline = Open();

        Assert.False(deadline.MarkBreached(Due - TimeSpan.FromTicks(1)));
        Assert.True(deadline.MarkBreached(Due));
        Assert.False(deadline.MarkBreached(Due + TimeSpan.FromMinutes(5)));
        Assert.Equal(Due, deadline.BreachedAt);
        Assert.True(deadline.AwaitsEscalation);
    }

    [Fact]
    public void A_closed_deadline_is_never_marked()
    {
        var deadline = Open();
        deadline.Close(DueSoon);

        Assert.False(deadline.MarkBreached(Due));
        Assert.False(deadline.IsUnmarkedBreachAt(Due));
        Assert.Null(deadline.BreachedAt);
    }

    [Fact]
    public void The_escalation_is_claimed_once_and_can_be_given_back()
    {
        var deadline = Open();
        deadline.MarkBreached(Due);

        deadline.ClaimEscalation(Due);
        Assert.Equal(Due, deadline.EscalatedAt);
        Assert.False(deadline.AwaitsEscalation);
        Assert.Throws<InvalidOperationException>(() => deadline.ClaimEscalation(Due));

        deadline.ReleaseEscalation();
        Assert.True(deadline.AwaitsEscalation);
    }

    [Fact]
    public void An_unmarked_deadline_cannot_be_escalated() =>
        Assert.Throws<InvalidOperationException>(() => Open().ClaimEscalation(Due));

    [Fact]
    public void Closing_twice_keeps_the_first_close()
    {
        var deadline = Open();

        deadline.Close(DueSoon);
        deadline.Close(Due);

        Assert.False(deadline.IsOpen);
        Assert.Equal(DueSoon, deadline.ClosedAt);
    }

    private static RecordDeadline Open() => RecordDeadline.Open(7, TestRecord.ReviewDefinition(), TestRecord.Start);
}

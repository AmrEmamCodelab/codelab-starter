using Starter.Domain.Auditing;

namespace Starter.Api.Tests.Auditing;

/// <summary>The record history base: what a new entry carries and the limits it refuses.</summary>
public sealed class RecordHistoryEntryTests
{
    private static readonly RecordHistoryFacts Facts = new(
        RecordId: 42,
        Action: "approve",
        FromStatus: 2,
        ToStatus: 3,
        Reason: "complete",
        ActorUserId: 7,
        ActorName: "Reviewer",
        At: new DateTimeOffset(2026, 11, 6, 11, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_new_entry_carries_every_fact()
    {
        var entry = new ProbeHistory(Facts);

        Assert.Equal(Facts.RecordId, entry.RecordId);
        Assert.Equal(Facts.Action, entry.Action);
        Assert.Equal(Facts.FromStatus, entry.FromStatus);
        Assert.Equal(Facts.ToStatus, entry.ToStatus);
        Assert.Equal(Facts.Reason, entry.Reason);
        Assert.Equal(Facts.ActorUserId, entry.ActorUserId);
        Assert.Equal(Facts.ActorName, entry.ActorName);
        Assert.Equal(Facts.At, entry.At);
    }

    [Fact]
    public void Facts_at_the_limits_are_accepted() =>
        RecordHistoryLimits.Check(Facts with
        {
            Action = new string('a', RecordHistoryLimits.ActionMaxLength),
            Reason = new string('r', RecordHistoryLimits.ReasonMaxLength),
            ActorName = new string('n', RecordHistoryLimits.ActorNameMaxLength),
        });

    [Fact]
    public void Blank_or_over_long_facts_are_refused()
    {
        Assert.Throws<ArgumentException>(() => new ProbeHistory(Facts with { Action = " " }));
        Assert.Throws<ArgumentException>(() => new ProbeHistory(Facts with { ActorName = "" }));
        Assert.Throws<ArgumentException>(() => new ProbeHistory(Facts with { Action = new string('a', RecordHistoryLimits.ActionMaxLength + 1) }));
        Assert.Throws<ArgumentException>(() => new ProbeHistory(Facts with { Reason = new string('r', RecordHistoryLimits.ReasonMaxLength + 1) }));
        Assert.Throws<ArgumentException>(() => new ProbeHistory(Facts with { ActorName = new string('n', RecordHistoryLimits.ActorNameMaxLength + 1) }));
        Assert.Throws<ArgumentNullException>(() => RecordHistoryLimits.Check(null!));
    }

    [Fact]
    public void History_rows_are_their_own_trail_and_mark_the_actor_and_reason_as_personal()
    {
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(ProbeHistory), typeof(NotAuditedAttribute), inherit: true));
        Assert.Equal(PersonalDataKind.Name, PersonalKind(nameof(RecordHistoryEntry.ActorName)));
        Assert.Equal(PersonalDataKind.FreeText, PersonalKind(nameof(RecordHistoryEntry.Reason)));
    }

    private static PersonalDataKind? PersonalKind(string property) =>
        (Attribute.GetCustomAttribute(typeof(RecordHistoryEntry).GetProperty(property)!, typeof(PersonalDataAttribute)) as PersonalDataAttribute)?.Kind;
}

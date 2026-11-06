using Starter.Domain.Records;

namespace Starter.Application.Records;

/// <summary>
/// The deadline of one status of one record type: a record that enters <see cref="Status"/> is due
/// <see cref="Duration"/> later, is "due soon" from <see cref="DueSoonBefore"/> before that, and raises
/// <see cref="EscalationTemplateKey"/> once when it breaches. Validated on construction.
/// </summary>
/// <param name="RecordType">The record type key (<see cref="RecordTypeKeyFormat"/>).</param>
/// <param name="Status">The status value the deadline runs in.</param>
/// <param name="Duration">The time allowed in the status (more than zero).</param>
/// <param name="DueSoonBefore">The due-soon window before the deadline (zero or more, less than <paramref name="Duration"/>).</param>
/// <param name="EscalationTemplateKey">The notification template raised on a breach.</param>
/// <exception cref="ArgumentException">The record type key or the template key is malformed.</exception>
/// <exception cref="ArgumentOutOfRangeException">A duration is out of range.</exception>
public sealed record SlaDefinition(
    string RecordType,
    int Status,
    TimeSpan Duration,
    TimeSpan DueSoonBefore,
    string EscalationTemplateKey)
{
    /// <summary>The record type key.</summary>
    public string RecordType { get; } = RecordTypeKeyFormat.EnsureValid(RecordType, nameof(RecordType));

    /// <summary>The time allowed in the status.</summary>
    public TimeSpan Duration { get; } = Duration > TimeSpan.Zero
        ? Duration
        : throw new ArgumentOutOfRangeException(nameof(Duration), Duration, "An SLA duration is more than zero.");

    /// <summary>The due-soon window before the deadline.</summary>
    public TimeSpan DueSoonBefore { get; } = DueSoonBefore >= TimeSpan.Zero && DueSoonBefore < Duration
        ? DueSoonBefore
        : throw new ArgumentOutOfRangeException(nameof(DueSoonBefore), DueSoonBefore, "The due-soon window is zero or more and shorter than the SLA duration.");

    /// <summary>The escalation template key.</summary>
    public string EscalationTemplateKey { get; } = string.IsNullOrWhiteSpace(EscalationTemplateKey)
        ? throw new ArgumentException("An SLA names its escalation notification template.", nameof(EscalationTemplateKey))
        : EscalationTemplateKey;

    /// <summary>
    /// The badge of a record that entered <see cref="Status"/> at <paramref name="enteredAt"/>, seen at
    /// <paramref name="now"/>: breached from the deadline on, due soon inside the window, otherwise on track. The one
    /// owner of the SLA state rule.
    /// </summary>
    public SlaBadge BadgeAt(DateTimeOffset enteredAt, DateTimeOffset now)
    {
        var dueAt = enteredAt + Duration;
        if (now >= dueAt)
        {
            return new SlaBadge(SlaState.Breached, dueAt);
        }

        return new SlaBadge(now >= dueAt - DueSoonBefore ? SlaState.DueSoon : SlaState.OnTrack, dueAt);
    }
}

/// <summary>
/// The SLA definitions one module owns (zero or more statuses of its record types). The owning module registers one
/// source; the Deadlines module reads every source.
/// </summary>
public interface ISlaPolicySource
{
    /// <summary>The module's SLA definitions; stable for the process lifetime, at most one per record type and status.</summary>
    IEnumerable<SlaDefinition> Definitions { get; }
}

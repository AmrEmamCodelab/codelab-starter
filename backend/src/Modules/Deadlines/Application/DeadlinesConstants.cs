using Starter.Application.Notifications;

namespace Starter.Modules.Deadlines.Application;

/// <summary>The keys of the jobs this module registers (<c>Starter.Application.Jobs.JobKeyFormat</c>).</summary>
public static class DeadlinesJobKeys
{
    /// <summary>
    /// Marks the breached deadlines and raises one escalation notification per breach. Kebab case: the job key format
    /// allows no dot, so the key is <c>deadlines-sla-scan</c>.
    /// </summary>
    public const string SlaScan = "deadlines-sla-scan";
}

/// <summary>The schedules of the jobs this module registers: five-field cron in the jobs time zone (<c>Jobs:TimeZoneId</c>).</summary>
public static class DeadlinesSchedules
{
    /// <summary>Every 5 minutes: a breach is escalated at most this long after its deadline.</summary>
    public const string EveryFiveMinutes = "*/5 * * * *";
}

/// <summary>
/// The escalation notification's shape: the placeholder names every escalation template uses (<c>{{name}}</c>), its
/// channels and the display format of the deadline.
/// </summary>
public static class DeadlinesNotifications
{
    /// <summary>The record's reference number (<c>PREFIX-YYYY-NNN</c>), or empty for a record without one.</summary>
    public const string RecordNumber = "recordNumber";

    /// <summary>The key of the status the deadline ran in (the workflow's status key, or its value when the record type declares no workflow).</summary>
    public const string StatusKey = "statusKey";

    /// <summary>The deadline in UTC, as <see cref="DueAtFormat"/>.</summary>
    public const string DueAt = "dueAt";

    /// <summary>The invariant display format of <see cref="DueAt"/> (UTC).</summary>
    public const string DueAtFormat = "yyyy-MM-dd HH:mm 'UTC'";

    /// <summary>Where an escalation is delivered: the bell and e-mail.</summary>
    public const NotificationChannels EscalationChannels = NotificationChannels.InApp | NotificationChannels.Email;
}

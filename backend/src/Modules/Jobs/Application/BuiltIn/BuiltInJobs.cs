namespace Starter.Modules.Jobs.Application.BuiltIn;

/// <summary>The keys of the jobs this module registers (<c>Starter.Application.Jobs.JobKeyFormat</c>).</summary>
public static class JobsJobKeys
{
    /// <summary>Delivers the due outbox messages (<c>IOutboxDrainRunner.DrainOnceAsync</c>).</summary>
    public const string OutboxDrain = "outbox-drain";

    /// <summary>Deletes expired rows of the Codelab.Security SQL stores (replay nonces, revoked sessions).</summary>
    public const string SecurityReplayPurge = "security-replay-purge";
}

/// <summary>The schedules of the jobs this module registers: five-field cron in the jobs time zone (<c>Jobs:TimeZoneId</c>).</summary>
public static class JobsSchedules
{
    /// <summary>Every 5 minutes: the outbox drain (a message waits at most this long plus its back-off).</summary>
    public const string EveryFiveMinutes = "*/5 * * * *";

    /// <summary>Daily at 02:00: the security store purge, outside office hours.</summary>
    public const string DailyAtTwoAm = "0 2 * * *";
}

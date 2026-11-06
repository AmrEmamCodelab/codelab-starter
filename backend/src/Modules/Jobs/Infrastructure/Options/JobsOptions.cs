using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Starter.Modules.Jobs.Infrastructure.Options;

/// <summary>The <c>Jobs</c> section: the job server of this node and the job schedules.</summary>
public sealed class JobsOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Jobs";

    /// <summary>The time zone of the schedules when none is configured (Riyadh, no daylight saving).</summary>
    public const string DefaultTimeZoneId = "Arab Standard Time";

    /// <summary>The job server's worker threads when none are configured.</summary>
    public const int DefaultWorkerCount = 5;

    /// <summary>The fewest workers.</summary>
    public const int MinWorkerCount = 1;

    /// <summary>The most workers (every one holds a SQL connection while it polls).</summary>
    public const int MaxWorkerCount = 20;

    /// <summary>
    /// True (default) to run the job server on this node: it executes scheduled and manual runs and keeps the recurring
    /// schedules up to date on start. False on a node that only serves requests; its admin triggers are still queued and
    /// run by a node where it is true. At least one node must have it on, or nothing runs (the outbox is not drained).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The worker threads of this node's job server (<see cref="MinWorkerCount"/> to <see cref="MaxWorkerCount"/>).</summary>
    public int WorkerCount { get; set; } = DefaultWorkerCount;

    /// <summary>
    /// The time zone the cron schedules are read in (a Windows or IANA id). Default <see cref="DefaultTimeZoneId"/>.
    /// </summary>
    public string TimeZoneId { get; set; } = DefaultTimeZoneId;

    /// <summary>
    /// True (default) to let the scheduler create or upgrade its tables in schema <c>hangfire</c> when it first
    /// connects (needs DDL rights). False where the DBA installs the schema; the scheduler then only uses it.
    /// </summary>
    public bool PrepareSchemaIfNecessary { get; set; } = true;

    /// <summary>The options as configured in <paramref name="configuration"/> (defaults for missing keys).</summary>
    public static JobsOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetSection(SectionName).Get<JobsOptions>() ?? new JobsOptions();
    }

    /// <summary>The schedules' time zone.</summary>
    /// <exception cref="TimeZoneNotFoundException">The id is unknown on this machine.</exception>
    public TimeZoneInfo ResolveTimeZone() => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
}

/// <summary>Validates <see cref="JobsOptions"/> on start.</summary>
public sealed class JobsOptionsValidator : IValidateOptions<JobsOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, JobsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.WorkerCount is < JobsOptions.MinWorkerCount or > JobsOptions.MaxWorkerCount)
        {
            return ValidateOptionsResult.Fail(
                $"{JobsOptions.SectionName}:{nameof(JobsOptions.WorkerCount)} must be {JobsOptions.MinWorkerCount} to {JobsOptions.MaxWorkerCount}.");
        }

        return IsKnownTimeZone(options.TimeZoneId)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{JobsOptions.SectionName}:{nameof(JobsOptions.TimeZoneId)} is not a time zone of this machine.");
    }

    private static bool IsKnownTimeZone(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _);
}

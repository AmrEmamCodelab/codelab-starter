using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Starter.Modules.Jobs.Application.Catalog;
using Starter.Modules.Jobs.Infrastructure.Options;

namespace Starter.Modules.Jobs.Infrastructure.Scheduling;

/// <summary>
/// Brings the scheduler's recurring jobs in line with the catalog when a job-server node starts (after the migration
/// startup tasks and <see cref="SchedulerSchema"/>, before its server starts): every job with a cron is added or updated under its key, in the <c>Jobs:TimeZoneId</c> time zone; every
/// other recurring job (a job removed from the code, or whose cron became null) is removed. Idempotent, so every node
/// may run it. An invalid cron stops the node, like any other invalid configuration.
/// </summary>
public sealed partial class RecurringJobRegistrar(
    IServiceScopeFactory scopes,
    IRecurringJobManager recurringJobs,
    JobStorage storage,
    IOptions<JobsOptions> options,
    ILogger<RecurringJobRegistrar> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<JobCatalog>();
        var timeZone = options.Value.ResolveTimeZone();
        var scheduled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var job in catalog.Jobs)
        {
            if (job.Cron is not { } cron)
            {
                continue;
            }

            var jobKey = job.Key;
            recurringJobs.AddOrUpdate<HangfireJobEntry>(
                jobKey,
                entry => entry.RunScheduledAsync(jobKey, CancellationToken.None),
                cron,
                new RecurringJobOptions { TimeZone = timeZone });
            scheduled.Add(jobKey);
            LogScheduled(logger, jobKey, cron);
        }

        RemoveUnscheduled(scheduled);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void RemoveUnscheduled(HashSet<string> scheduled)
    {
        using var connection = storage.GetConnection();
        var stale = connection.GetRecurringJobs().Select(job => job.Id).Where(id => !scheduled.Contains(id)).ToList();
        foreach (var id in stale)
        {
            recurringJobs.RemoveIfExists(id);
            LogRemoved(logger, id);
        }
    }

    [LoggerMessage(EventId = 6319, Level = LogLevel.Information, Message = "Job {JobKey} scheduled ({Cron})")]
    private static partial void LogScheduled(ILogger logger, string jobKey, string cron);

    [LoggerMessage(EventId = 6320, Level = LogLevel.Information, Message = "Recurring job {RecurringJobId} removed: no registered job schedules it")]
    private static partial void LogRemoved(ILogger logger, string recurringJobId);
}

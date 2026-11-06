using Hangfire;
using Hangfire.Storage;
using Starter.Modules.Jobs.Application;
using Starter.Modules.Jobs.Application.Execution;

namespace Starter.Modules.Jobs.Infrastructure.Scheduling;

/// <summary>
/// What the scheduler stores and calls: Hangfire serializes a call to one of these methods (type, method, arguments) and
/// resolves this class from a fresh DI scope on a job server. Keep the type and method names stable: queued and
/// recurring jobs refer to them by name. Hangfire passes its own shutdown token for the <see cref="CancellationToken"/>.
/// Failures are recorded on the run row by <see cref="JobExecution"/>, so the scheduler never retries a run; an
/// infrastructure failure reaching Hangfire is logged by the job failure filter and the job is deleted.
/// </summary>
public sealed class HangfireJobEntry(JobRunner runner)
{
    /// <summary>A recurring tick of the job <paramref name="jobKey"/>.</summary>
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public Task RunScheduledAsync(string jobKey, CancellationToken cancellationToken) =>
        runner.RunScheduledAsync(jobKey, cancellationToken);

    /// <summary>The queued manual run <paramref name="runId"/>.</summary>
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public Task RunManualAsync(long runId, CancellationToken cancellationToken) =>
        runner.RunManualAsync(runId, cancellationToken);
}

/// <summary><see cref="IJobRunDispatcher"/> on Hangfire's background job client (the run executes on any node with a job server).</summary>
public sealed class HangfireJobRunDispatcher(IBackgroundJobClient client) : IJobRunDispatcher
{
    /// <inheritdoc />
    public void DispatchManualRun(long runId) =>
        client.Enqueue<HangfireJobEntry>(entry => entry.RunManualAsync(runId, CancellationToken.None));
}

/// <summary>
/// <see cref="IJobRunLock"/> on the scheduler storage's distributed lock (in SQL Server an application lock held by a
/// dedicated connection), so the lock spans every node. It does not wait: a held lock means a run is executing.
/// </summary>
public sealed class HangfireRunLock(JobStorage storage) : IJobRunLock
{
    /// <summary>The prefix of every job's lock resource.</summary>
    public const string ResourcePrefix = "starter:jobs:";

    /// <summary>How long an acquisition waits for a lock someone holds.</summary>
    public static readonly TimeSpan AcquireTimeout = TimeSpan.FromSeconds(1);

    /// <inheritdoc />
    public Task<IAsyncDisposable?> TryAcquireAsync(string jobKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobKey);
        cancellationToken.ThrowIfCancellationRequested();
        var connection = storage.GetConnection();
        IDisposable? handle = null;
        try
        {
            handle = TryLock(connection, ResourcePrefix + jobKey);
        }
        finally
        {
            if (handle is null)
            {
                connection.Dispose();
            }
        }

        return Task.FromResult<IAsyncDisposable?>(handle is null ? null : new HeldLock(handle, connection));
    }

    private static IDisposable? TryLock(IStorageConnection connection, string resource)
    {
        try
        {
            return connection.AcquireDistributedLock(resource, AcquireTimeout);
        }
        catch (DistributedLockTimeoutException)
        {
            return null; // another run of the job holds it
        }
    }

    private sealed class HeldLock(IDisposable handle, IStorageConnection connection) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            try
            {
                handle.Dispose();
            }
            finally
            {
                connection.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }
}

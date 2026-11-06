using System.Reflection;
using Codelab.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Starter.Application.Jobs;
using Starter.Application.Users;
using Starter.Infrastructure.Users;
using Starter.Modules.Jobs.Application;
using Starter.Modules.Jobs.Application.Catalog;
using Starter.Modules.Jobs.Application.Execution;
using Starter.Modules.Jobs.Domain;

namespace Starter.Modules.Jobs.Tests.Fakes;

/// <summary>An in-memory run history: ids are assigned on save, like an identity column.</summary>
internal sealed class FakeJobRuns : IJobRuns
{
    private static readonly PropertyInfo IdProperty = typeof(JobRun).GetProperty(nameof(JobRun.Id))!;
    private long _nextId = 100;

    public List<JobRun> Runs { get; } = [];

    public int Saves { get; private set; }

    /// <summary>Set to fail the next save with this exception.</summary>
    public Exception? FailNextSave { get; set; }

    /// <summary>Raised after each save, with the saved runs (for tests that wait on a background run).</summary>
    public event Action<IReadOnlyList<JobRun>>? Saved;

    public JobRun Seed(JobRun run)
    {
        Runs.Add(run);
        AssignIds();
        return run;
    }

    public void Add(JobRun run) => Runs.Add(run);

    public Task<JobRun?> FindAsync(long runId, CancellationToken cancellationToken) =>
        Task.FromResult(Runs.SingleOrDefault(run => run.Id == runId));

    public Task<bool> HasActiveRunAsync(string jobKey, DateTimeOffset since, CancellationToken cancellationToken) =>
        Task.FromResult(Runs.Any(run => run.JobKey == jobKey
            && run.Status is JobRunStatus.Queued or JobRunStatus.Running
            && run.QueuedAt >= since));

    public Task<IReadOnlyDictionary<string, JobRun>> LatestPerJobAsync(IReadOnlyCollection<string> jobKeys, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, JobRun> latest = Runs
            .Where(run => jobKeys.Contains(run.JobKey))
            .GroupBy(run => run.JobKey)
            .ToDictionary(group => group.Key, group => group.MaxBy(run => run.Id)!, StringComparer.Ordinal);
        return Task.FromResult(latest);
    }

    public Task<IReadOnlyList<JobRun>> ListRecentAsync(string jobKey, int take, CancellationToken cancellationToken)
    {
        IReadOnlyList<JobRun> recent = [.. Runs.Where(run => run.JobKey == jobKey).OrderByDescending(run => run.Id).Take(take)];
        return Task.FromResult(recent);
    }

    public Task<int> AbandonOtherRunningAsync(JobRun current, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var others = Runs.Where(run => run.JobKey == current.JobKey && run.Id != current.Id && run.Status is JobRunStatus.Running).ToList();
        foreach (var run in others)
        {
            run.Fail(new JobRunFailure(JobRunFailureCodes.RunAbandoned, ReferenceCode: null), at);
        }

        return Task.FromResult(others.Count);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailNextSave is { } failure)
        {
            FailNextSave = null;
            throw failure;
        }

        Saves++;
        AssignIds();
        Saved?.Invoke([.. Runs]);
        return Task.CompletedTask;
    }

    private void AssignIds()
    {
        foreach (var run in Runs.Where(run => run.Id == 0))
        {
            IdProperty.SetValue(run, _nextId++);
        }
    }
}

/// <summary>A job whose behaviour each test chooses; it records what it saw while running.</summary>
internal sealed class FakeJob(string key, string? cron = SampleJobs.Cron, Func<JobRunContext, CancellationToken, Task>? body = null) : IJob
{
    public string Key => key;

    public string NameAr => "مهمة تجريبية";

    public string NameEn => "Sample job";

    public string? Cron => cron;

    public int Runs { get; private set; }

    public JobRunContext? LastContext { get; private set; }

    public bool SawSystemScope { get; private set; }

    public string? SawCorrelationId { get; private set; }

    public async Task RunAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        Runs++;
        LastContext = context;
        SawSystemScope = SystemActorScope.IsActiveOnCurrentFlow;
        SawCorrelationId = CorrelationScope.Current;
        if (body is not null)
        {
            await body(context, cancellationToken);
        }
    }
}

internal static class SampleJobs
{
    public const string Key = "sample-job";
    public const string OtherKey = "other-job";
    public const string Cron = "*/10 * * * *";
}

internal sealed class FakeRunLock : IJobRunLock
{
    public HashSet<string> HeldElsewhere { get; } = new(StringComparer.Ordinal);

    public List<string> Acquired { get; } = [];

    public int Released { get; private set; }

    public Task<IAsyncDisposable?> TryAcquireAsync(string jobKey, CancellationToken cancellationToken)
    {
        if (HeldElsewhere.Contains(jobKey))
        {
            return Task.FromResult<IAsyncDisposable?>(null);
        }

        Acquired.Add(jobKey);
        return Task.FromResult<IAsyncDisposable?>(new Release(this));
    }

    private sealed class Release(FakeRunLock owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.Released++;
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class FakeDispatcher : IJobRunDispatcher
{
    public List<long> Dispatched { get; } = [];

    public Exception? Failure { get; set; }

    public void DispatchManualRun(long runId)
    {
        if (Failure is { } failure)
        {
            throw failure;
        }

        Dispatched.Add(runId);
    }
}

internal sealed class FakeCurrentUser(Guid? subjectId) : ICurrentUserContext
{
    public bool IsAuthenticated => subjectId is not null;

    public Guid? SubjectId => subjectId;

    public string? SessionId => null;
}

/// <summary>Maps <see cref="TimeoutException"/> to a code of its own and everything else to the internal error.</summary>
internal sealed class FakeFailureCodes : IFailureCodeResolver
{
    public const string TimeoutCode = "TEST_TIMEOUT";

    public FailureCode Resolve(Exception? exception) => exception is TimeoutException
        ? new FailureCode(TimeoutCode, LogLevel.Warning)
        : new FailureCode(DefaultFailureCodeResolver.InternalErrorCode, LogLevel.Error);
}

/// <summary>The runner over fakes, with a controllable clock.</summary>
internal sealed class RunnerFixture
{
    public static readonly DateTimeOffset Start = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    public RunnerFixture(params IJob[] jobs)
    {
        Catalog = new JobCatalog(jobs);
        Execution = new JobExecution(Runs, new SystemActorScope(), new FakeFailureCodes(), Clock, NullLogger<JobExecution>.Instance);
        Runner = new JobRunner(Catalog, Runs, Lock, Execution, NullLogger<JobRunner>.Instance);
    }

    public FakeTimeProvider Clock { get; } = new(Start);

    public FakeJobRuns Runs { get; } = new();

    public FakeRunLock Lock { get; } = new();

    public JobCatalog Catalog { get; }

    public JobExecution Execution { get; }

    public JobRunner Runner { get; }

    /// <summary>A manual run queued now (saved, so it has an id).</summary>
    public JobRun QueueManual(string jobKey, Guid? subject = null) =>
        Runs.Seed(JobRun.Queue(jobKey, JobTrigger.Manual, subject, Clock.GetUtcNow()));
}

/// <summary>The kernel user directory: subjects registered with a display name; any other subject is unknown.</summary>
public sealed class FakeUserDirectory : IUserDirectory
{
    public Dictionary<Guid, string> Names { get; } = [];

    public List<Guid> Asked { get; } = [];

    public Task<DirectoryUser?> FindBySubjectAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        Asked.Add(subjectId);
        return Task.FromResult(Names.TryGetValue(subjectId, out var name)
            ? new DirectoryUser(UserId: Names.Count, IsActive: true, HoldsAllPermissions: false, DisplayName: name)
            : null);
    }
}

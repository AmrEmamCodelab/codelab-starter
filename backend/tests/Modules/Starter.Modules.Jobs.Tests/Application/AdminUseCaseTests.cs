using Microsoft.Extensions.Time.Testing;
using Starter.Application.Jobs;
using Starter.Application.Results;
using Starter.Modules.Jobs.Api;
using Starter.Modules.Jobs.Application;
using Starter.Modules.Jobs.Application.Admin;
using Starter.Modules.Jobs.Application.Catalog;
using Starter.Modules.Jobs.Domain;
using Starter.Modules.Jobs.Tests.Fakes;

namespace Starter.Modules.Jobs.Tests.Application;

public sealed class AdminUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeJobRuns _runs = new();
    private readonly FakeDispatcher _dispatcher = new();
    private readonly JobCatalog _catalog = new([new FakeJob(SampleJobs.Key), new FakeJob(SampleJobs.OtherKey, cron: null)]);
    private readonly FakeUserDirectory _users = new();

    private JobRunViews Views => new(_users);

    [Fact]
    public async Task ListJobs_returns_every_job_with_its_newest_run()
    {
        _runs.Seed(JobRun.Queue(SampleJobs.Key, JobTrigger.Schedule, triggeredBySubjectId: null, Now));
        var newest = _runs.Seed(JobRun.Queue(SampleJobs.Key, JobTrigger.Manual, triggeredBySubjectId: null, Now));

        var outcome = await new ListJobs(_catalog, _runs, Views).ExecuteAsync(CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal([SampleJobs.OtherKey, SampleJobs.Key], outcome.Value.Select(summary => summary.Job.Key));
        Assert.Null(outcome.Value[0].LastRun);
        Assert.Same(newest, outcome.Value[1].LastRun!.Run);
    }

    [Fact]
    public async Task The_history_names_the_administrator_who_ran_a_job_now_and_never_the_subject_id()
    {
        var admin = Guid.NewGuid();
        var gone = Guid.NewGuid();
        _users.Names[admin] = "Break-glass administrator";
        _runs.Seed(JobRun.Queue(SampleJobs.Key, JobTrigger.Schedule, triggeredBySubjectId: null, Now));
        _runs.Seed(JobRun.Queue(SampleJobs.Key, JobTrigger.Manual, triggeredBySubjectId: admin, Now));
        _runs.Seed(JobRun.Queue(SampleJobs.Key, JobTrigger.Manual, triggeredBySubjectId: admin, Now));
        _runs.Seed(JobRun.Queue(SampleJobs.Key, JobTrigger.Manual, triggeredBySubjectId: gone, Now));

        var views = (await new ListJobRuns(_catalog, _runs, Views).ExecuteAsync(SampleJobs.Key, new JobRunsQuery(Take: null), CancellationToken.None)).Value;
        var wire = JobRunResponse.ListFrom(views);

        Assert.Equal([null, "Break-glass administrator", "Break-glass administrator", null], wire.Reverse().Select(run => run.TriggeredBy));
        Assert.DoesNotContain(wire, run => run.TriggeredBy == admin.ToString());
        Assert.Equal(new[] { admin, gone }.Order(), _users.Asked.Order()); // each subject resolved once
    }

    [Fact]
    public async Task Trigger_records_a_queued_manual_run_by_the_caller_and_dispatches_it()
    {
        var subject = Guid.NewGuid();

        var outcome = await Trigger(subject).ExecuteAsync(SampleJobs.Key, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        var run = Assert.Single(_runs.Runs);
        Assert.Same(run, outcome.Value);
        Assert.Equal(JobRunStatus.Queued, run.Status);
        Assert.Equal(JobTrigger.Manual, run.Trigger);
        Assert.Equal(SystemActor.Name, run.Actor);
        Assert.Equal(subject, run.TriggeredBySubjectId);
        Assert.Equal(Now, run.QueuedAt);
        Assert.Equal([run.Id], _dispatcher.Dispatched);
    }

    [Fact]
    public async Task Trigger_of_an_unknown_job_is_404()
    {
        var outcome = await Trigger().ExecuteAsync("missing-job", CancellationToken.None);

        AssertRefused(outcome, RefusalCategory.NotFound, JobsErrorCodes.JobNotFound);
        Assert.Empty(_runs.Runs);
        Assert.Empty(_dispatcher.Dispatched);
    }

    [Theory]
    [InlineData(JobRunStatus.Queued)]
    [InlineData(JobRunStatus.Running)]
    public async Task Trigger_while_a_run_is_active_is_409(JobRunStatus status)
    {
        var active = _runs.Seed(JobRun.Queue(SampleJobs.Key, JobTrigger.Schedule, triggeredBySubjectId: null, Now.AddMinutes(-5)));
        if (status is JobRunStatus.Running)
        {
            active.Start("corr-0001", Now.AddMinutes(-5));
        }

        var outcome = await Trigger().ExecuteAsync(SampleJobs.Key, CancellationToken.None);

        AssertRefused(outcome, RefusalCategory.Conflict, JobsErrorCodes.RunInProgress);
        Assert.Single(_runs.Runs);
    }

    [Fact]
    public async Task A_run_older_than_the_active_window_no_longer_blocks_a_trigger()
    {
        _runs.Seed(JobRun.Queue(SampleJobs.Key, JobTrigger.Manual, triggeredBySubjectId: null, Now - JobsLimits.ActiveRunWindow - TimeSpan.FromSeconds(1)));

        var outcome = await Trigger().ExecuteAsync(SampleJobs.Key, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(2, _runs.Runs.Count);
    }

    [Fact]
    public async Task A_run_of_another_job_does_not_block_a_trigger()
    {
        _runs.Seed(JobRun.Queue(SampleJobs.OtherKey, JobTrigger.Manual, triggeredBySubjectId: null, Now));

        var outcome = await Trigger().ExecuteAsync(SampleJobs.Key, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
    }

    [Fact]
    public async Task A_dispatch_failure_closes_the_run_and_fails_the_call()
    {
        _dispatcher.Failure = new TimeoutException("scheduler storage unreachable");

        await Assert.ThrowsAsync<TimeoutException>(() => Trigger().ExecuteAsync(SampleJobs.Key, CancellationToken.None));

        var run = Assert.Single(_runs.Runs);
        Assert.Equal(JobRunStatus.Failed, run.Status);
        Assert.Equal(JobRunFailureCodes.EnqueueFailed, run.ErrorCode);
        Assert.Null(run.ReferenceCode);
        Assert.Equal(2, _runs.Saves);
    }

    [Fact]
    public async Task ListJobRuns_returns_the_newest_runs_first_with_the_default_take()
    {
        for (var index = 0; index < JobsLimits.DefaultHistoryTake + 5; index++)
        {
            _runs.Seed(JobRun.Queue(SampleJobs.Key, JobTrigger.Schedule, triggeredBySubjectId: null, Now));
        }

        var outcome = await new ListJobRuns(_catalog, _runs, Views).ExecuteAsync(SampleJobs.Key, new JobRunsQuery(Take: null), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(JobsLimits.DefaultHistoryTake, outcome.Value.Count);
        Assert.True(outcome.Value[0].Run.Id > outcome.Value[^1].Run.Id);
    }

    [Fact]
    public async Task ListJobRuns_honours_take()
    {
        _runs.Seed(JobRun.Queue(SampleJobs.Key, JobTrigger.Schedule, triggeredBySubjectId: null, Now));
        var newest = _runs.Seed(JobRun.Queue(SampleJobs.Key, JobTrigger.Schedule, triggeredBySubjectId: null, Now));

        var outcome = await new ListJobRuns(_catalog, _runs, Views).ExecuteAsync(SampleJobs.Key, new JobRunsQuery(Take: 1), CancellationToken.None);

        Assert.Same(newest, Assert.Single(outcome.Value).Run);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(JobsLimits.MaxHistoryTake + 1)]
    public async Task ListJobRuns_refuses_a_take_out_of_range_as_a_field_error(int take)
    {
        var outcome = await new ListJobRuns(_catalog, _runs, Views).ExecuteAsync(SampleJobs.Key, new JobRunsQuery(take), CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(RefusalCategory.Invalid, outcome.Refusal.Category);
        Assert.Equal([JobsErrorCodes.TakeOutOfRange], outcome.Refusal.FieldErrors["take"]);
    }

    [Fact]
    public async Task ListJobRuns_of_an_unknown_job_is_404()
    {
        var outcome = await new ListJobRuns(_catalog, _runs, Views).ExecuteAsync("missing-job", new JobRunsQuery(Take: null), CancellationToken.None);

        AssertRefused(outcome, RefusalCategory.NotFound, JobsErrorCodes.JobNotFound);
    }

    private TriggerJobRun Trigger(Guid? subject = null) =>
        new(_catalog, _runs, _dispatcher, new FakeCurrentUser(subject ?? Guid.NewGuid()), _clock);

    private static void AssertRefused<T>(Outcome<T> outcome, RefusalCategory category, string code)
    {
        Assert.False(outcome.IsSuccess);
        Assert.Equal(category, outcome.Refusal.Category);
        Assert.Equal(code, outcome.Refusal.Code);
    }
}

using Codelab.Observability;
using Starter.Application.Jobs;
using Starter.Infrastructure.Users;
using Starter.Modules.Jobs.Application;
using Starter.Modules.Jobs.Domain;
using Starter.Modules.Jobs.Tests.Fakes;

namespace Starter.Modules.Jobs.Tests.Application;

public sealed class JobRunnerTests
{
    [Fact]
    public async Task A_scheduled_tick_records_a_succeeded_run_as_the_system_actor()
    {
        var job = new FakeJob(SampleJobs.Key);
        var fixture = new RunnerFixture(job);

        await fixture.Runner.RunScheduledAsync(SampleJobs.Key, CancellationToken.None);

        var run = Assert.Single(fixture.Runs.Runs);
        Assert.Equal(JobRunStatus.Succeeded, run.Status);
        Assert.Equal(JobTrigger.Schedule, run.Trigger);
        Assert.Equal("SYSTEM", run.Actor);
        Assert.Null(run.TriggeredBySubjectId);
        Assert.Equal(RunnerFixture.Start, run.StartedAt);
        Assert.Equal(RunnerFixture.Start, run.FinishedAt);
        Assert.Null(run.ErrorCode);
        Assert.Null(run.ReferenceCode);
        Assert.Equal(1, job.Runs);
        Assert.Equal(new JobRunContext(SampleJobs.Key, run.Id, JobTrigger.Schedule, run.CorrelationId!), job.LastContext);
        Assert.Equal([SampleJobs.Key], fixture.Lock.Acquired);
        Assert.Equal(1, fixture.Lock.Released);
    }

    [Fact]
    public async Task The_job_runs_inside_a_system_scope_and_its_own_correlation_scope()
    {
        var job = new FakeJob(SampleJobs.Key);
        var fixture = new RunnerFixture(job);

        await fixture.Runner.RunScheduledAsync(SampleJobs.Key, CancellationToken.None);

        var run = Assert.Single(fixture.Runs.Runs);
        Assert.True(job.SawSystemScope);
        Assert.Matches("^[0-9a-f]{32}$", run.CorrelationId);
        Assert.Equal(run.CorrelationId, job.SawCorrelationId);
        Assert.False(SystemActorScope.IsActiveOnCurrentFlow);
        Assert.Null(CorrelationScope.Current);
    }

    [Fact]
    public async Task A_queued_manual_run_runs_and_keeps_who_triggered_it()
    {
        var subject = Guid.NewGuid();
        var job = new FakeJob(SampleJobs.Key);
        var fixture = new RunnerFixture(job);
        var queued = fixture.QueueManual(SampleJobs.Key, subject);

        await fixture.Runner.RunManualAsync(queued.Id, CancellationToken.None);

        Assert.Equal(JobRunStatus.Succeeded, queued.Status);
        Assert.Equal(JobTrigger.Manual, queued.Trigger);
        Assert.Equal(SystemActor.Name, queued.Actor);
        Assert.Equal(subject, queued.TriggeredBySubjectId);
        Assert.True(job.SawSystemScope);
        Assert.Single(fixture.Runs.Runs);
    }

    [Fact]
    public async Task A_failing_job_ends_failed_with_the_classified_code_and_a_reference_code_never_the_message()
    {
        const string secretText = "request REQ-0042 of a named person not found";
        var job = new FakeJob(SampleJobs.Key, body: (_, _) => throw new InvalidOperationException(secretText));
        var fixture = new RunnerFixture(job);

        await fixture.Runner.RunScheduledAsync(SampleJobs.Key, CancellationToken.None);

        var run = Assert.Single(fixture.Runs.Runs);
        Assert.Equal(JobRunStatus.Failed, run.Status);
        Assert.Equal(DefaultFailureCodeResolver.InternalErrorCode, run.ErrorCode);
        Assert.Equal(ReferenceCode.From(run.CorrelationId!), run.ReferenceCode);
        Assert.Matches("^[0-9A-F]{5}-[0-9A-F]{5}$", run.ReferenceCode);
        Assert.DoesNotContain(secretText, run.ErrorCode, StringComparison.Ordinal);
        Assert.Equal(1, fixture.Lock.Released);
    }

    [Fact]
    public async Task A_classified_failure_keeps_its_own_code()
    {
        var job = new FakeJob(SampleJobs.Key, body: (_, _) => throw new TimeoutException("slow"));
        var fixture = new RunnerFixture(job);

        await fixture.Runner.RunScheduledAsync(SampleJobs.Key, CancellationToken.None);

        Assert.Equal(FakeFailureCodes.TimeoutCode, Assert.Single(fixture.Runs.Runs).ErrorCode);
    }

    [Fact]
    public async Task A_job_cancelling_on_its_own_is_a_failed_run()
    {
        var job = new FakeJob(SampleJobs.Key, body: (_, _) => throw new TaskCanceledException("own timeout"));
        var fixture = new RunnerFixture(job);

        await fixture.Runner.RunScheduledAsync(SampleJobs.Key, CancellationToken.None);

        Assert.Equal(JobRunStatus.Failed, Assert.Single(fixture.Runs.Runs).Status);
    }

    [Fact]
    public async Task The_node_stopping_propagates_and_leaves_the_run_running()
    {
        using var stopping = new CancellationTokenSource();
        var job = new FakeJob(SampleJobs.Key, body: (_, token) =>
        {
            stopping.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
        var fixture = new RunnerFixture(job);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Runner.RunScheduledAsync(SampleJobs.Key, stopping.Token));

        Assert.Equal(JobRunStatus.Running, Assert.Single(fixture.Runs.Runs).Status);
        Assert.Equal(1, fixture.Lock.Released);
    }

    [Fact]
    public async Task The_outcome_is_saved_even_when_the_node_stops_after_the_job_returned()
    {
        using var stopping = new CancellationTokenSource();
        var job = new FakeJob(SampleJobs.Key, body: (_, _) =>
        {
            stopping.Cancel();
            return Task.CompletedTask;
        });
        var fixture = new RunnerFixture(job);

        await fixture.Runner.RunScheduledAsync(SampleJobs.Key, stopping.Token);

        Assert.Equal(JobRunStatus.Succeeded, Assert.Single(fixture.Runs.Runs).Status);
    }

    [Fact]
    public async Task Runs_a_stopped_node_left_running_are_closed_as_abandoned()
    {
        var fixture = new RunnerFixture(new FakeJob(SampleJobs.Key));
        var stale = fixture.QueueManual(SampleJobs.Key);
        stale.Start("corr-stale", RunnerFixture.Start.AddHours(-2));
        var waiting = fixture.QueueManual(SampleJobs.Key);

        await fixture.Runner.RunScheduledAsync(SampleJobs.Key, CancellationToken.None);

        Assert.Equal(JobRunStatus.Failed, stale.Status);
        Assert.Equal(JobRunFailureCodes.RunAbandoned, stale.ErrorCode);
        Assert.Equal(JobRunStatus.Queued, waiting.Status);
    }

    [Fact]
    public async Task A_tick_while_the_job_executes_elsewhere_is_skipped_without_a_row()
    {
        var job = new FakeJob(SampleJobs.Key);
        var fixture = new RunnerFixture(job);
        fixture.Lock.HeldElsewhere.Add(SampleJobs.Key);

        await fixture.Runner.RunScheduledAsync(SampleJobs.Key, CancellationToken.None);

        Assert.Empty(fixture.Runs.Runs);
        Assert.Equal(0, job.Runs);
    }

    [Fact]
    public async Task A_tick_of_a_job_no_longer_registered_does_nothing()
    {
        var fixture = new RunnerFixture(new FakeJob(SampleJobs.Key));

        await fixture.Runner.RunScheduledAsync("removed-job", CancellationToken.None);

        Assert.Empty(fixture.Runs.Runs);
        Assert.Empty(fixture.Lock.Acquired);
    }

    [Fact]
    public async Task A_manual_run_while_the_job_executes_elsewhere_fails_as_overlapped()
    {
        var job = new FakeJob(SampleJobs.Key);
        var fixture = new RunnerFixture(job);
        var queued = fixture.QueueManual(SampleJobs.Key);
        fixture.Lock.HeldElsewhere.Add(SampleJobs.Key);

        await fixture.Runner.RunManualAsync(queued.Id, CancellationToken.None);

        Assert.Equal(JobRunStatus.Failed, queued.Status);
        Assert.Equal(JobRunFailureCodes.RunOverlapped, queued.ErrorCode);
        Assert.Equal(0, job.Runs);
    }

    [Fact]
    public async Task A_manual_run_of_a_job_no_longer_registered_fails()
    {
        var fixture = new RunnerFixture(new FakeJob(SampleJobs.Key));
        var queued = fixture.Runs.Seed(JobRun.Queue("removed-job", JobTrigger.Manual, triggeredBySubjectId: null, RunnerFixture.Start));

        await fixture.Runner.RunManualAsync(queued.Id, CancellationToken.None);

        Assert.Equal(JobRunStatus.Failed, queued.Status);
        Assert.Equal(JobRunFailureCodes.JobNotRegistered, queued.ErrorCode);
    }

    [Fact]
    public async Task A_repeated_delivery_of_a_finished_or_missing_run_does_nothing()
    {
        var job = new FakeJob(SampleJobs.Key);
        var fixture = new RunnerFixture(job);
        var queued = fixture.QueueManual(SampleJobs.Key);
        await fixture.Runner.RunManualAsync(queued.Id, CancellationToken.None);
        var saves = fixture.Runs.Saves;

        await fixture.Runner.RunManualAsync(queued.Id, CancellationToken.None);
        await fixture.Runner.RunManualAsync(runId: 999_999, CancellationToken.None);

        Assert.Equal(1, job.Runs);
        Assert.Equal(saves, fixture.Runs.Saves);
    }

    [Fact]
    public async Task A_redelivered_running_manual_run_runs_again()
    {
        var job = new FakeJob(SampleJobs.Key);
        var fixture = new RunnerFixture(job);
        var queued = fixture.QueueManual(SampleJobs.Key);
        queued.Start("corr-crashed", RunnerFixture.Start);

        await fixture.Runner.RunManualAsync(queued.Id, CancellationToken.None);

        Assert.Equal(JobRunStatus.Succeeded, queued.Status);
        Assert.NotEqual("corr-crashed", queued.CorrelationId);
    }
}

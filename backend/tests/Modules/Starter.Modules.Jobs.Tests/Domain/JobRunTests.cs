using Starter.Application.Jobs;
using Starter.Modules.Jobs.Domain;

namespace Starter.Modules.Jobs.Tests.Domain;

public sealed class JobRunTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_queued_run_is_recorded_under_the_system_actor_with_who_triggered_it()
    {
        var subject = Guid.NewGuid();

        var run = JobRun.Queue("outbox-drain", JobTrigger.Manual, subject, At);

        Assert.Equal(JobRunStatus.Queued, run.Status);
        Assert.Equal(JobTrigger.Manual, run.Trigger);
        Assert.Equal(SystemActor.Name, run.Actor);
        Assert.Equal("SYSTEM", run.Actor);
        Assert.Equal(subject, run.TriggeredBySubjectId);
        Assert.Equal(At, run.QueuedAt);
        Assert.Null(run.StartedAt);
        Assert.False(run.IsFinished);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Outbox-Drain")]
    [InlineData("outbox drain")]
    public void A_malformed_key_is_refused(string key) =>
        Assert.Throws<ArgumentException>(() => JobRun.Queue(key, JobTrigger.Schedule, triggeredBySubjectId: null, At));

    [Fact]
    public void An_unknown_trigger_is_refused() =>
        Assert.Throws<ArgumentException>(() => JobRun.Queue("outbox-drain", (JobTrigger)99, triggeredBySubjectId: null, At));

    [Fact]
    public void Start_then_succeed_records_the_times_and_the_correlation_id()
    {
        var run = JobRun.Queue("outbox-drain", JobTrigger.Schedule, triggeredBySubjectId: null, At);

        run.Start("corr-0001", At.AddSeconds(1));
        run.Succeed(At.AddSeconds(2));

        Assert.Equal(JobRunStatus.Succeeded, run.Status);
        Assert.Equal("corr-0001", run.CorrelationId);
        Assert.Equal(At.AddSeconds(1), run.StartedAt);
        Assert.Equal(At.AddSeconds(2), run.FinishedAt);
        Assert.Null(run.ErrorCode);
        Assert.True(run.IsFinished);
    }

    [Fact]
    public void A_running_run_may_start_again_after_a_redelivery()
    {
        var run = JobRun.Queue("outbox-drain", JobTrigger.Manual, triggeredBySubjectId: null, At);
        run.Start("corr-first", At);

        run.Start("corr-second", At.AddMinutes(5));

        Assert.Equal(JobRunStatus.Running, run.Status);
        Assert.Equal("corr-second", run.CorrelationId);
        Assert.Equal(At.AddMinutes(5), run.StartedAt);
    }

    [Fact]
    public void Fail_keeps_only_the_code_and_the_reference_code()
    {
        var run = JobRun.Queue("outbox-drain", JobTrigger.Schedule, triggeredBySubjectId: null, At);
        run.Start("corr-0001", At);

        run.Fail(new JobRunFailure("INTERNAL_ERROR", "9C1E5-B7A20"), At.AddSeconds(3));

        Assert.Equal(JobRunStatus.Failed, run.Status);
        Assert.Equal("INTERNAL_ERROR", run.ErrorCode);
        Assert.Equal("9C1E5-B7A20", run.ReferenceCode);
        Assert.Equal(At.AddSeconds(3), run.FinishedAt);
    }

    [Fact]
    public void A_queued_run_can_fail_without_starting()
    {
        var run = JobRun.Queue("outbox-drain", JobTrigger.Manual, triggeredBySubjectId: null, At);

        run.Fail(new JobRunFailure("JOBS_RUN_OVERLAPPED", ReferenceCode: null), At);

        Assert.Equal(JobRunStatus.Failed, run.Status);
        Assert.Null(run.StartedAt);
        Assert.Null(run.ReferenceCode);
    }

    [Fact]
    public void Codes_longer_than_their_columns_are_cut()
    {
        var run = JobRun.Queue("outbox-drain", JobTrigger.Manual, triggeredBySubjectId: null, At);

        run.Fail(new JobRunFailure(new string('X', JobsLimits.ErrorCodeMaxLength + 10), new string('R', JobsLimits.ReferenceCodeMaxLength + 1)), At);

        Assert.Equal(JobsLimits.ErrorCodeMaxLength, run.ErrorCode!.Length);
        Assert.Equal(JobsLimits.ReferenceCodeMaxLength, run.ReferenceCode!.Length);
    }

    [Fact]
    public void Only_a_running_run_can_succeed()
    {
        var run = JobRun.Queue("outbox-drain", JobTrigger.Manual, triggeredBySubjectId: null, At);

        Assert.Throws<InvalidOperationException>(() => run.Succeed(At));
    }

    [Fact]
    public void A_finished_run_never_changes_again()
    {
        var run = JobRun.Queue("outbox-drain", JobTrigger.Manual, triggeredBySubjectId: null, At);
        run.Start("corr-0001", At);
        run.Succeed(At);

        Assert.Throws<InvalidOperationException>(() => run.Start("corr-0002", At));
        Assert.Throws<InvalidOperationException>(() => run.Fail(new JobRunFailure("INTERNAL_ERROR", ReferenceCode: null), At));
        Assert.Throws<InvalidOperationException>(() => run.Succeed(At));
    }

    [Fact]
    public void A_failure_needs_a_code()
    {
        var run = JobRun.Queue("outbox-drain", JobTrigger.Manual, triggeredBySubjectId: null, At);

        Assert.Throws<ArgumentException>(() => run.Fail(new JobRunFailure(" ", ReferenceCode: null), At));
    }
}

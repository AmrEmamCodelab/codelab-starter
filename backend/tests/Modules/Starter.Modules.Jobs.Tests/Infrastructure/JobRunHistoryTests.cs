using Starter.Application.Jobs;
using Starter.Modules.Jobs.Domain;
using Starter.Modules.Jobs.Infrastructure.Persistence;
using Starter.Modules.Jobs.Tests.Fakes;

namespace Starter.Modules.Jobs.Tests.Infrastructure;

public sealed class JobRunHistoryTests
{
    private static readonly DateTimeOffset Queued = new(2026, 11, 7, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Started = Queued.AddSeconds(1);
    private static readonly DateTimeOffset Finished = Queued.AddSeconds(4);

    private readonly FakeJobRuns _runs = new();

    [Fact]
    public async Task The_newest_succeeded_run_is_summarized()
    {
        var older = _runs.Seed(JobRun.Queue(SampleJobs.Key, JobTrigger.Schedule, triggeredBySubjectId: null, Queued));
        older.Start("c0ffee", Started);
        older.Fail(new JobRunFailure("INTERNAL_ERROR", "OLD-REF"), Finished);
        var newest = _runs.Seed(JobRun.Queue(SampleJobs.Key, JobTrigger.Schedule, triggeredBySubjectId: null, Queued));
        newest.Start("c0ffee", Started);
        newest.Succeed(Finished);

        var latest = await new JobRunHistory(_runs).LatestPerJobAsync([SampleJobs.Key], CancellationToken.None);

        Assert.Equal(new JobRunSummary(JobRunStatus.Succeeded, JobTrigger.Schedule, Started, Finished, ReferenceCode: null), latest[SampleJobs.Key]);
    }

    [Fact]
    public async Task A_failed_run_keeps_its_reference_code_and_a_job_that_never_ran_is_absent()
    {
        var failed = _runs.Seed(JobRun.Queue(SampleJobs.Key, JobTrigger.Manual, triggeredBySubjectId: Guid.NewGuid(), Queued));
        failed.Start("c0ffee", Started);
        failed.Fail(new JobRunFailure("INTERNAL_ERROR", "9C1E5-B7A20"), Finished);

        var latest = await new JobRunHistory(_runs).LatestPerJobAsync([SampleJobs.Key, SampleJobs.OtherKey], CancellationToken.None);

        Assert.Equal(new JobRunSummary(JobRunStatus.Failed, JobTrigger.Manual, Started, Finished, "9C1E5-B7A20"), latest[SampleJobs.Key]);
        Assert.False(latest.ContainsKey(SampleJobs.OtherKey));
    }

    [Fact]
    public async Task No_keys_read_nothing()
    {
        _runs.Seed(JobRun.Queue(SampleJobs.Key, JobTrigger.Schedule, triggeredBySubjectId: null, Queued));

        Assert.Empty(await new JobRunHistory(_runs).LatestPerJobAsync([], CancellationToken.None));
    }
}

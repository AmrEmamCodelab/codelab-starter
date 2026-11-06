using Codelab.Security.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Starter.Application.Jobs;
using Starter.Application.Messaging;
using Starter.Modules.Jobs.Application.BuiltIn;
using Starter.Modules.Jobs.Application.Catalog;
using Starter.Modules.Jobs.Infrastructure.BuiltIn;
using Starter.Modules.Jobs.Tests.Fakes;

namespace Starter.Modules.Jobs.Tests.Application;

public sealed class JobKeyFormatTests
{
    [Theory]
    [InlineData("outbox-drain")]
    [InlineData("security-replay-purge")]
    [InlineData("abc")]
    [InlineData("job2-run3")]
    public void Kebab_keys_are_valid(string key) => Assert.True(JobKeyFormat.IsValid(key));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("Outbox")]
    [InlineData("2-jobs")]
    [InlineData("-job")]
    [InlineData("job-")]
    [InlineData("job--run")]
    [InlineData("job_run")]
    [InlineData("job.run")]
    public void Other_keys_are_invalid(string? key) => Assert.False(JobKeyFormat.IsValid(key));

    [Fact]
    public void The_longest_key_fits_the_limit()
    {
        Assert.True(JobKeyFormat.IsValid(new string('a', JobKeyFormat.MaxLength)));
        Assert.False(JobKeyFormat.IsValid(new string('a', JobKeyFormat.MaxLength + 1)));
    }
}

public sealed class JobCatalogTests
{
    [Fact]
    public void Jobs_are_ordered_by_key_and_found_by_exact_key()
    {
        var catalog = new JobCatalog([new FakeJob("zeta-job"), new FakeJob("alpha-job")]);

        Assert.Equal(["alpha-job", "zeta-job"], catalog.Keys);
        Assert.Equal("zeta-job", catalog.Find("zeta-job")!.Key);
        Assert.Null(catalog.Find("ZETA-JOB"));
        Assert.Null(catalog.Find(null));
        Assert.Null(catalog.Find("missing-job"));
    }

    [Fact]
    public void Two_jobs_with_one_key_are_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new JobCatalog([new FakeJob("same-job"), new FakeJob("same-job")]));

        Assert.Contains("same-job", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_key_is_refused() =>
        Assert.Throws<InvalidOperationException>(() => new JobCatalog([new FakeJob("Bad Key")]));

    [Fact]
    public void A_blank_schedule_is_refused_but_null_means_manual_only()
    {
        Assert.Throws<InvalidOperationException>(() => new JobCatalog([new FakeJob("blank-cron", cron: " ")]));

        var manual = new JobCatalog([new FakeJob("manual-only", cron: null)]);
        Assert.Null(manual.Find("manual-only")!.Cron);
    }

    [Fact]
    public void A_job_without_both_names_is_refused() =>
        Assert.Throws<InvalidOperationException>(() => new JobCatalog([new NamelessJob()]));

    [Fact]
    public void The_built_in_jobs_form_a_valid_catalog_with_their_schedules()
    {
        var catalog = new JobCatalog([BuiltIn.OutboxDrain(), BuiltIn.SecurityPurge()]);

        Assert.Equal([JobsJobKeys.OutboxDrain, JobsJobKeys.SecurityReplayPurge], catalog.Keys);
        Assert.Equal("outbox-drain", JobsJobKeys.OutboxDrain);
        Assert.Equal("security-replay-purge", JobsJobKeys.SecurityReplayPurge);
        Assert.Equal("*/5 * * * *", catalog.Find(JobsJobKeys.OutboxDrain)!.Cron);
        Assert.Equal("0 2 * * *", catalog.Find(JobsJobKeys.SecurityReplayPurge)!.Cron);
        Assert.All(catalog.Jobs, job => Assert.False(string.IsNullOrWhiteSpace(job.NameAr)));
    }

    private sealed class NamelessJob : IJob
    {
        public string Key => "nameless-job";

        public string NameAr => "اسم";

        public string NameEn => " ";

        public string? Cron => null;

        public Task RunAsync(JobRunContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

/// <summary>The built-in jobs over fakes of their dependencies.</summary>
internal static class BuiltIn
{
    public static OutboxDrainJob OutboxDrain(FakeDrain? drain = null) =>
        new(drain ?? new FakeDrain(), NullLogger<OutboxDrainJob>.Instance);

    public static SecurityReplayPurgeJob SecurityPurge(params IExpiredEntryPurger[] purgers) =>
        new(new SecurityStorePurgeJob(purgers), NullLogger<SecurityReplayPurgeJob>.Instance);
}

internal sealed class FakeDrain : IOutboxDrainRunner
{
    public int Passes { get; private set; }

    public Exception? Failure { get; set; }

    public Task<OutboxDrainResult> DrainOnceAsync(CancellationToken cancellationToken)
    {
        Passes++;
        return Failure is { } failure ? Task.FromException<OutboxDrainResult>(failure) : Task.FromResult(new OutboxDrainResult(Sent: 2, Retried: 1, Failed: 0));
    }
}

internal sealed class FakePurger(string storeName, int removed, Exception? failure = null) : IExpiredEntryPurger
{
    public int Calls { get; private set; }

    public string StoreName => storeName;

    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return failure is null ? Task.FromResult(removed) : Task.FromException<int>(failure);
    }
}

public sealed class BuiltInJobTests
{
    private static readonly JobRunContext Context = new("outbox-drain", RunId: 7, JobTrigger.Schedule, CorrelationId: "corr-0001");

    [Fact]
    public async Task The_outbox_drain_job_runs_one_drain_pass()
    {
        var drain = new FakeDrain();

        await BuiltIn.OutboxDrain(drain).RunAsync(Context, CancellationToken.None);

        Assert.Equal(1, drain.Passes);
    }

    [Fact]
    public async Task A_failing_drain_pass_fails_the_run()
    {
        var drain = new FakeDrain { Failure = new TimeoutException("sql timeout") };

        await Assert.ThrowsAsync<TimeoutException>(() => BuiltIn.OutboxDrain(drain).RunAsync(Context, CancellationToken.None));
    }

    [Fact]
    public async Task The_security_purge_job_purges_every_store()
    {
        var replay = new FakePurger("replay", removed: 3);
        var sessions = new FakePurger("sessions", removed: 0);

        await BuiltIn.SecurityPurge(replay, sessions).RunAsync(Context with { JobKey = JobsJobKeys.SecurityReplayPurge }, CancellationToken.None);

        Assert.Equal(1, replay.Calls);
        Assert.Equal(1, sessions.Calls);
    }

    [Fact]
    public async Task A_failing_store_fails_the_purge_run_after_the_others_ran()
    {
        var broken = new FakePurger("replay", removed: 0, new TimeoutException("sql timeout"));
        var sessions = new FakePurger("sessions", removed: 4);

        await Assert.ThrowsAsync<AggregateException>(() => BuiltIn.SecurityPurge(broken, sessions).RunAsync(Context, CancellationToken.None));
        Assert.Equal(1, sessions.Calls);
    }
}

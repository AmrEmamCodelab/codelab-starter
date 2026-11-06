using Codelab.Observability;
using Codelab.Security.Storage;
using Hangfire;
using Hangfire.Common;
using Hangfire.InMemory;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Starter.Application.Jobs;
using Starter.Application.Messaging;
using Starter.Application.Users;
using Starter.Modules.Jobs.Api;
using Starter.Modules.Jobs.Application;
using Starter.Modules.Jobs.Application.Admin;
using Starter.Modules.Jobs.Application.BuiltIn;
using Starter.Modules.Jobs.Application.Catalog;
using Starter.Modules.Jobs.Application.Execution;
using Starter.Modules.Jobs.Domain;
using Starter.Modules.Jobs.Infrastructure;
using Starter.Modules.Jobs.Infrastructure.Options;
using Starter.Modules.Jobs.Infrastructure.Scheduling;
using Starter.Modules.Jobs.Tests.Application;
using Starter.Modules.Jobs.Tests.Fakes;

namespace Starter.Modules.Jobs.Tests.Infrastructure;

/// <summary>
/// Hangfire keeps its log provider in static state, set from the last container that applied its configuration; tests
/// that build containers therefore run one at a time.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HangfireTestGroup
{
    public const string Name = "hangfire";
}

/// <summary>The jobs module composed like the host does, on Hangfire's in-memory storage and the fake run history.</summary>
internal sealed class SchedulerHost : IAsyncDisposable
{
    private readonly List<IHostedService> _started = [];

    private SchedulerHost(ServiceProvider services) => Services = services;

    public ServiceProvider Services { get; }

    public JobStorage Storage => Services.GetRequiredService<JobStorage>();

    public FakeJobRuns Runs => Services.GetRequiredService<FakeJobRuns>();

    public static SchedulerHost Create(bool enabled, params IJob[] jobs)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Starter"] = "Server=.;Database=NeverOpened",
                ["Jobs:Enabled"] = enabled.ToString(),
                ["Jobs:WorkerCount"] = "1",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<JobStorage>(provider =>
        {
            provider.GetRequiredService<IGlobalConfiguration>();
            return new InMemoryStorage();
        });
        services.AddJobsModule(configuration);
        services.AddSingleton<FakeJobRuns>();
        services.Replace(ServiceDescriptor.Scoped<IJobRuns>(provider => provider.GetRequiredService<FakeJobRuns>()));
        services.AddScoped<ICurrentUserContext>(_ => new FakeCurrentUser(Guid.NewGuid()));
        services.AddScoped<IUserDirectory, FakeUserDirectory>();
        services.AddScoped<IOutboxDrainRunner, FakeDrain>();
        services.AddSingleton(new SecurityStorePurgeJob([]));
        foreach (var job in jobs)
        {
            services.AddScoped(_ => job);
        }

        return new SchedulerHost(services.BuildServiceProvider(validateScopes: true));
    }

    public async Task StartAsync()
    {
        foreach (var hosted in Services.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
            _started.Add(hosted);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var hosted in Enumerable.Reverse(_started))
        {
            await hosted.StopAsync(CancellationToken.None);
        }

        await Services.DisposeAsync();
    }
}

[Collection(HangfireTestGroup.Name)]
public sealed class JobsRegistrationTests
{
    [Fact]
    public async Task A_node_with_jobs_disabled_runs_no_job_server_but_can_queue_runs()
    {
        await using var host = SchedulerHost.Create(enabled: false);

        Assert.Empty(host.Services.GetServices<IHostedService>()); // nothing touches SQL at start; the storage prepares itself on first use
        await using var scope = host.Services.CreateAsyncScope();
        Assert.IsType<HangfireJobRunDispatcher>(scope.ServiceProvider.GetRequiredService<IJobRunDispatcher>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TriggerJobRun>());
    }

    [Fact]
    public async Task A_node_with_jobs_enabled_installs_the_schema_then_registers_the_schedules_before_the_job_server()
    {
        await using var host = SchedulerHost.Create(enabled: true);

        var hosted = host.Services.GetServices<IHostedService>().ToList();

        Assert.Equal(3, hosted.Count);
        Assert.IsType<SchedulerSchema>(hosted[0]);
        Assert.IsType<RecurringJobRegistrar>(hosted[1]);
    }

    [Fact]
    public async Task The_schema_installer_leaves_a_non_SQL_storage_alone()
    {
        await using var host = SchedulerHost.Create(enabled: true);

        Assert.False(host.Services.GetRequiredService<SchedulerSchema>().EnsureInstalled()); // never opens "NeverOpened"
    }

    [Fact]
    public async Task The_catalog_holds_the_built_in_jobs_and_every_use_case_resolves()
    {
        await using var host = SchedulerHost.Create(enabled: false);
        await using var scope = host.Services.CreateAsyncScope();

        var catalog = scope.ServiceProvider.GetRequiredService<JobCatalog>();

        Assert.Equal([JobsJobKeys.OutboxDrain, JobsJobKeys.SecurityReplayPurge], catalog.Keys);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ListJobs>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ListJobRuns>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<JobRunner>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISystemActorScope>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<HangfireJobEntry>());
    }

    [Fact]
    public void The_options_are_bound_and_validated()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jobs:WorkerCount"] = "0" })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJobsModule(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<JobsOptions>>().Value);
    }

    [Fact]
    public void The_failure_log_filter_runs_on_every_job_after_the_default_filters()
    {
        var filter = new JobFailureLogFilter(NullLoggerFactory.Instance, new DefaultFailureCodeResolver());
        var job = Job.FromExpression<HangfireJobEntry>(entry => entry.RunManualAsync(1, CancellationToken.None));

        var filters = new JobsFilterProvider(filter).GetFilters(job).ToList();

        Assert.Contains(filters, candidate => ReferenceEquals(candidate.Instance, filter));
        Assert.Contains(filters, candidate => candidate.Instance is AutomaticRetryAttribute { Attempts: 0 });
    }
}

[Collection(HangfireTestGroup.Name)]
public sealed class HangfireAdapterTests
{
    [Fact]
    public async Task A_manual_run_is_queued_as_a_call_of_the_job_entry()
    {
        await using var host = SchedulerHost.Create(enabled: false);
        var dispatcher = host.Services.GetRequiredService<IBackgroundJobClient>();

        new HangfireJobRunDispatcher(dispatcher).DispatchManualRun(runId: 42);

        var enqueued = host.Storage.GetMonitoringApi().EnqueuedJobs(EnqueuedState.DefaultQueue, from: 0, perPage: 10);
        var job = Assert.Single(enqueued).Value.Job;
        Assert.Equal(typeof(HangfireJobEntry), job.Type);
        Assert.Equal(nameof(HangfireJobEntry.RunManualAsync), job.Method.Name);
        Assert.Equal(42L, job.Args[0]);
    }

    [Fact]
    public async Task The_registrar_schedules_every_job_with_a_cron_in_the_jobs_time_zone_and_removes_stale_schedules()
    {
        await using var host = SchedulerHost.Create(enabled: false, new FakeJob(SampleJobs.Key, cron: null));
        var recurring = host.Services.GetRequiredService<IRecurringJobManager>();
        recurring.AddOrUpdate<HangfireJobEntry>("removed-job", entry => entry.RunScheduledAsync("removed-job", CancellationToken.None), Cron.Daily());

        await Registrar(host).StartAsync(CancellationToken.None);

        using var connection = host.Storage.GetConnection();
        var schedules = connection.GetRecurringJobs().ToDictionary(job => job.Id, StringComparer.Ordinal);
        Assert.Equal([JobsJobKeys.OutboxDrain, JobsJobKeys.SecurityReplayPurge], schedules.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(JobsSchedules.EveryFiveMinutes, schedules[JobsJobKeys.OutboxDrain].Cron);
        Assert.Equal(JobsSchedules.DailyAtTwoAm, schedules[JobsJobKeys.SecurityReplayPurge].Cron);
        Assert.Equal(nameof(HangfireJobEntry.RunScheduledAsync), schedules[JobsJobKeys.OutboxDrain].Job.Method.Name);
        Assert.Equal(JobsJobKeys.OutboxDrain, schedules[JobsJobKeys.OutboxDrain].Job.Args[0]);
        Assert.Equal(new JobsOptions().ResolveTimeZone().Id, schedules[JobsJobKeys.OutboxDrain].TimeZoneId);
    }

    [Fact]
    public async Task Registering_twice_changes_nothing()
    {
        await using var host = SchedulerHost.Create(enabled: false);

        await Registrar(host).StartAsync(CancellationToken.None);
        await Registrar(host).StartAsync(CancellationToken.None);

        using var connection = host.Storage.GetConnection();
        Assert.Equal(2, connection.GetRecurringJobs().Count);
    }

    [Fact]
    public async Task The_run_lock_admits_one_holder_per_job_key()
    {
        await using var host = SchedulerHost.Create(enabled: false);
        var runLock = new HangfireRunLock(host.Storage);

        await using (var first = await runLock.TryAcquireAsync(SampleJobs.Key, CancellationToken.None))
        {
            Assert.NotNull(first);
            Assert.Null(await TryFromAnotherThreadAsync(runLock, SampleJobs.Key));
            await using var other = await TryFromAnotherThreadAsync(runLock, SampleJobs.OtherKey);
            Assert.NotNull(other);
        }

        await using var again = await TryFromAnotherThreadAsync(runLock, SampleJobs.Key);
        Assert.NotNull(again);
    }

    [Fact]
    public async Task A_manual_run_runs_end_to_end_on_a_job_server_as_the_system_actor()
    {
        var job = new FakeJob(SampleJobs.Key);
        await using var host = SchedulerHost.Create(enabled: true, job);
        var finished = new TaskCompletionSource<JobRun>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Runs.Saved += runs =>
        {
            if (runs.FirstOrDefault(run => run.Trigger is JobTrigger.Manual && run.IsFinished) is { } done)
            {
                finished.TrySetResult(done);
            }
        };
        await host.StartAsync();
        var subject = Guid.NewGuid();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var trigger = new TriggerJobRun(
                scope.ServiceProvider.GetRequiredService<JobCatalog>(),
                scope.ServiceProvider.GetRequiredService<IJobRuns>(),
                scope.ServiceProvider.GetRequiredService<IJobRunDispatcher>(),
                new FakeCurrentUser(subject),
                TimeProvider.System);
            Assert.True((await trigger.ExecuteAsync(SampleJobs.Key, CancellationToken.None)).IsSuccess);
        }

        var run = await finished.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(JobRunStatus.Succeeded, run.Status);
        Assert.Equal(SystemActor.Name, run.Actor);
        Assert.Equal(subject, run.TriggeredBySubjectId);
        Assert.True(job.SawSystemScope);
        Assert.Equal("SYSTEM", JobRunResponse.From(new JobRunView(run, TriggeredByName: null)).Actor);
    }

    private static RecurringJobRegistrar Registrar(SchedulerHost host) => new(
        host.Services.GetRequiredService<IServiceScopeFactory>(),
        host.Services.GetRequiredService<IRecurringJobManager>(),
        host.Services.GetRequiredService<JobStorage>(),
        host.Services.GetRequiredService<IOptions<JobsOptions>>(),
        NullLogger<RecurringJobRegistrar>.Instance);

    // The in-memory storage's lock is re-entrant per thread, like SQL Server's per session: contend from another thread.
    private static Task<IAsyncDisposable?> TryFromAnotherThreadAsync(HangfireRunLock runLock, string jobKey) =>
        Task.Factory.StartNew(
            () => runLock.TryAcquireAsync(jobKey, CancellationToken.None).GetAwaiter().GetResult(),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
}

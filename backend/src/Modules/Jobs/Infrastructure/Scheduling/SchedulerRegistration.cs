using Codelab.Observability;
using Hangfire;
using Hangfire.Common;
using Hangfire.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Jobs.Application;
using Starter.Modules.Jobs.Infrastructure.Options;

namespace Starter.Modules.Jobs.Infrastructure.Scheduling;

/// <summary>
/// The Hangfire wiring of the jobs module: SQL Server storage in schema <see cref="SchemaName"/> of the application
/// database, the job failure log filter (Codelab.Observability) on every job, the client every node uses to queue a
/// manual run, and, only where <c>Jobs:Enabled</c> is true, the start-up step that installs the scheduler schema after
/// the migrations (<see cref="SchedulerSchema"/>), the recurring-schedule registrar and the job server. The
/// Hangfire dashboard is never mapped. Nothing here touches Hangfire's static configuration beyond its serializer
/// settings, so the storage and filters come from DI (a test replaces <see cref="JobStorage"/>).
/// </summary>
public static class SchedulerRegistration
{
    /// <summary>The database schema of the scheduler's own tables (not EF's: the scheduler installs them).</summary>
    public const string SchemaName = "hangfire";

    /// <summary>The longest a batch of scheduler commands may run.</summary>
    public static readonly TimeSpan CommandBatchMaxTimeout = TimeSpan.FromMinutes(5);

    /// <summary>How long a fetched job stays invisible to other servers before it is considered abandoned.</summary>
    public static readonly TimeSpan SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Registers the scheduler for <paramref name="options"/> (the switch is read once, at registration).</summary>
    public static IServiceCollection AddJobScheduler(this IServiceCollection services, JobsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        services.TryAddSingleton(CreateStorage);
        services.TryAddSingleton<IJobFilterProvider>(provider => new JobsFilterProvider(provider.GetRequiredService<JobFailureLogFilter>()));
        services.AddHangfire(configuration => configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings());
        services.AddScoped<HangfireJobEntry>();
        services.AddScoped<IJobRunDispatcher, HangfireJobRunDispatcher>();
        services.AddSingleton<IJobRunLock, HangfireRunLock>();
        if (options.Enabled)
        {
            services.AddSingleton<SchedulerSchema>();
            services.AddHostedService(provider => provider.GetRequiredService<SchedulerSchema>());
            services.AddHostedService<RecurringJobRegistrar>();
            services.AddHangfireServer((provider, server) =>
                server.WorkerCount = provider.GetRequiredService<IOptions<JobsOptions>>().Value.WorkerCount);
        }

        return services;
    }

    private static JobStorage CreateStorage(IServiceProvider provider)
    {
        // Applies the AddHangfire configuration (serializer settings, the host's logger) before the storage logs anything.
        provider.GetRequiredService<IGlobalConfiguration>();
        var connectionString = provider.GetRequiredService<IOptions<ConnectionStringsOptions>>().Value.Starter;
        var jobs = provider.GetRequiredService<IOptions<JobsOptions>>().Value;
        return new SqlServerStorage(connectionString, new SqlServerStorageOptions
        {
            SchemaName = SchemaName,
            // A job-server node resolves the storage while the host is built, before the migrations may have created the
            // database, so SchedulerSchema installs the schema there; any other node first resolves it on a request.
            PrepareSchemaIfNecessary = jobs.PrepareSchemaIfNecessary && !jobs.Enabled,
            SqlClientFactory = Microsoft.Data.SqlClient.SqlClientFactory.Instance,
            CommandBatchMaxTimeout = CommandBatchMaxTimeout,
            SlidingInvisibilityTimeout = SlidingInvisibilityTimeout,
            QueuePollInterval = TimeSpan.Zero,
            UseRecommendedIsolationLevel = true,
            DisableGlobalLocks = true,
        });
    }
}

/// <summary>
/// The job filters: Hangfire's defaults (global filters and the attributes on job methods) plus the
/// <see cref="JobFailureLogFilter"/>, which gives a job failure that reaches the scheduler the same one-line structured
/// log as an HTTP failure. Composed per container instead of added to Hangfire's static global filters.
/// </summary>
public sealed class JobsFilterProvider(JobFailureLogFilter failureLog) : IJobFilterProvider
{
    /// <inheritdoc />
    public IEnumerable<JobFilter> GetFilters(Job job) =>
        JobFilterProviders.Providers.GetFilters(job).Append(new JobFilter(failureLog, JobFilterScope.Global, order: null));
}

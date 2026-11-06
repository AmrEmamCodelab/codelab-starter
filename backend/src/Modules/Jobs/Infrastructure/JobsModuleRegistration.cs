using Codelab.Observability;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Starter.Application.Authorization;
using Starter.Infrastructure.Jobs;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Jobs.Application;
using Starter.Modules.Jobs.Application.Admin;
using Starter.Modules.Jobs.Application.BuiltIn;
using Starter.Modules.Jobs.Application.Catalog;
using Starter.Modules.Jobs.Application.Execution;
using Starter.Modules.Jobs.Infrastructure.BuiltIn;
using Starter.Modules.Jobs.Infrastructure.Options;
using Starter.Modules.Jobs.Infrastructure.Persistence;
using Starter.Modules.Jobs.Infrastructure.Scheduling;

namespace Starter.Modules.Jobs.Infrastructure;

/// <summary>The jobs module's registrations (called by <see cref="JobsModule"/>).</summary>
public static class JobsModuleRegistration
{
    /// <summary>
    /// Registers the permission catalog source, the <c>Jobs</c> options (validated on start), the DbContext in schema
    /// <c>jobs</c> with the run history, the catalog, the use cases, the runner, the two built-in jobs
    /// (<c>outbox-drain</c>, <c>security-replay-purge</c>) and the scheduler (Hangfire on schema <c>hangfire</c>; the job
    /// server only where <c>Jobs:Enabled</c> is true). The jobs come from every module's <c>AddStarterJob</c>.
    /// </summary>
    public static IServiceCollection AddJobsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, JobsPermissionCatalogSource>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<JobsOptions>().Bind(configuration.GetSection(JobsOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<JobsOptions>, JobsOptionsValidator>());
        services.AddModuleDbContext<JobsDbContext>(JobsDbContext.SchemaName);
        services.AddScoped<IJobRuns, JobRunStore>();
        services.AddStarterSystemActor();
        services.TryAddSingleton<IFailureCodeResolver, DefaultFailureCodeResolver>();
        services.TryAddSingleton<JobFailureLogFilter>();
        AddUseCases(services);
        services.AddStarterJob<OutboxDrainJob>();
        services.AddStarterJob<SecurityReplayPurgeJob>();
        services.AddJobScheduler(JobsOptions.From(configuration));
        // scaffold:registrations
        return services;
    }

    /// <summary>Maps nothing extra: every jobs endpoint is a controller, found by the kernel; the dashboard is never mapped.</summary>
    public static IEndpointRouteBuilder UseJobsModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints;
    }

    private static void AddUseCases(IServiceCollection services)
    {
        services.AddScoped<JobCatalog>();
        services.AddScoped<ListJobs>();
        services.AddScoped<TriggerJobRun>();
        services.AddScoped<ListJobRuns>();
        services.AddScoped<JobRunViews>();
        services.AddScoped<JobExecution>();
        services.AddScoped<JobRunner>();
    }
}

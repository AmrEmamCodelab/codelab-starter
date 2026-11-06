using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Starter.Application.Records;
using Starter.Infrastructure.Jobs;
using Starter.Infrastructure.Persistence;
using Starter.Infrastructure.Startup;
using Starter.Infrastructure.Workflow;
using Starter.Modules.Deadlines.Application;
using Starter.Modules.Deadlines.Infrastructure.Persistence;

namespace Starter.Modules.Deadlines.Infrastructure;

/// <summary>The deadlines module's registrations (called by <see cref="DeadlinesModule"/>).</summary>
public static class DeadlinesModuleRegistration
{
    /// <summary>
    /// Registers the DbContext in schema <c>deadlines</c>, the SLA catalog over every <see cref="ISlaPolicySource"/>
    /// (singleton) with its startup check, the transition observer, the SLA badge reader, the escalation pipeline and the
    /// <c>deadlines-sla-scan</c> job. No endpoint: runs and schedules are the Jobs module's.
    /// </summary>
    public static IServiceCollection AddDeadlinesModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddModuleDbContext<DeadlinesDbContext>(DeadlinesDbContext.SchemaName);
        services.AddScoped<IRecordDeadlineStore, RecordDeadlineStore>();
        services.AddRecordWorkflowKernel();
        services.TryAddSingleton<SlaPolicyCatalog>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, SlaPolicyStartupCheck>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRecordTransitionObserver, DeadlineTransitionObserver>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRecordSlaReader, RecordSlaReader>());
        services.AddScoped<EscalationRecipients>();
        services.AddScoped<BreachLoader>();
        services.AddScoped<SlaEscalationNotifier>();
        services.AddScoped<SlaEscalations>();
        services.AddScoped<SlaScan>();
        services.AddStarterJob<SlaScanJob>();
        // scaffold:registrations
        return services;
    }

    /// <summary>Maps nothing: the module has no endpoint.</summary>
    public static IEndpointRouteBuilder UseDeadlinesModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints;
    }
}

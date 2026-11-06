using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Application.Validation;
using Starter.Infrastructure.Persistence;
using Starter.Infrastructure.Workflow;
using Starter.Modules.Assignments.Application;
using Starter.Modules.Assignments.Application.Commands;
using Starter.Modules.Assignments.Application.Queues;
using Starter.Modules.Assignments.Infrastructure.Persistence;

namespace Starter.Modules.Assignments.Infrastructure;

/// <summary>The Assignments module's registrations (called by <see cref="AssignmentsModule"/>).</summary>
public static class AssignmentsModuleRegistration
{
    /// <summary>
    /// Registers the permission catalog source, the <c>assignments</c> DbContext and store, the queue services and use
    /// cases, the validators, the kernel <see cref="IRecordAssignmentReader"/> and the terminal-status
    /// <see cref="IRecordTransitionObserver"/>. The host must also hold a module that implements the kernel
    /// <see cref="IRecordDirectory"/> (Records); <see cref="IAssigneeEligibility"/> defaults to the fail-closed
    /// <see cref="UnresolvedAssigneeEligibility"/> unless one is registered first.
    /// </summary>
    public static IServiceCollection AddAssignmentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, AssignmentsPermissionCatalogSource>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddRecordWorkflowKernel();
        services.AddModuleDbContext<AssignmentsDbContext>(AssignmentsDbContext.SchemaName);
        services.AddScoped<IAssignmentStore, AssignmentStore>();
        services.TryAddScoped<IAssigneeEligibility, UnresolvedAssigneeEligibility>();
        AddQueues(services);
        AddCommands(services);
        // scaffold:registrations
        return services;
    }

    /// <summary>Maps nothing extra: every Assignments endpoint is a controller, found by the kernel.</summary>
    public static IEndpointRouteBuilder UseAssignmentsModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints;
    }

    private static void AddQueues(IServiceCollection services)
    {
        services.AddScoped<CurrentStaff>();
        services.AddScoped<RecordQueues>();
        services.AddScoped<UserDisplayNames>();
        services.AddScoped<RecordAssignmentReader>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRecordAssignmentReader, RecordAssignmentReader>());
        services.AddScoped<UnassignedQueuePages>();
        services.AddScoped<WorkQueueViews>();
        services.AddScoped<WorkQueueEntries>();
        services.AddScoped<IValidator<WorkQueueRequest>, WorkQueueRequestValidator>();
        services.AddScoped<GetWorkQueuePage>();
    }

    private static void AddCommands(IServiceCollection services)
    {
        services.AddScoped<AssignmentRows>();
        services.AddScoped<IValidator<AssignRecordCommand>, AssignRecordValidator>();
        services.AddScoped<ClaimRecord>();
        services.AddScoped<ReleaseRecord>();
        services.AddScoped<AssignRecord>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRecordTransitionObserver, CloseAssignmentOnTerminal>());
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Starter.Application.Workflow;

namespace Starter.Infrastructure.Workflow;

/// <summary>How a module registers the workflow of its record type.</summary>
public static class WorkflowRegistration
{
    /// <summary>
    /// Registers <typeparamref name="TDefinition"/> (singleton), the catalog registry
    /// (<see cref="IRecordWorkflowCatalogs"/>), the open generic <see cref="IRecordWorkflow{TRecord}"/> facade (scoped;
    /// the owning module registers its <see cref="IWorkflowRecordReader{TRecord}"/>) and the startup check that fails
    /// the boot on any catalog violation. Idempotent per definition type.
    /// </summary>
    public static IServiceCollection AddRecordWorkflow<TDefinition>(this IServiceCollection services)
        where TDefinition : class, IRecordWorkflowDefinition
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddRecordWorkflowKernel();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRecordWorkflowDefinition, TDefinition>());
        return services;
    }

    /// <summary>
    /// Registers the registry, the facade and the startup check without a definition (a host with zero workflows,
    /// or a module that resolves the registry before any workflow module is added). Idempotent.
    /// </summary>
    public static IServiceCollection AddRecordWorkflowKernel(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IRecordWorkflowCatalogs, RecordWorkflowCatalogs>();
        services.TryAdd(ServiceDescriptor.Scoped(typeof(IRecordWorkflow<>), typeof(RecordWorkflow<>)));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WorkflowCatalogStartupCheck>());
        return services;
    }
}

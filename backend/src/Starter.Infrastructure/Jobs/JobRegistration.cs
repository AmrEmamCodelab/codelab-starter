using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Starter.Application.Jobs;
using Starter.Infrastructure.Users;

namespace Starter.Infrastructure.Jobs;

/// <summary>How any module contributes a background job (the jobs module schedules, runs and records it).</summary>
public static class JobRegistration
{
    /// <summary>
    /// Registers <typeparamref name="TJob"/> as an <see cref="IJob"/>, scoped (one instance per run, so it may inject
    /// scoped ports and DbContexts), and the <see cref="ISystemActorScope"/> the runs execute in. Registering the same
    /// job type again does nothing. Two job types with one key make the jobs module refuse to start its catalog.
    /// </summary>
    public static IServiceCollection AddStarterJob<TJob>(this IServiceCollection services)
        where TJob : class, IJob
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddStarterSystemActor();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IJob, TJob>());
        return services;
    }

    /// <summary>Registers the <see cref="ISystemActorScope"/> (a singleton over an async-local flag). Idempotent.</summary>
    public static IServiceCollection AddStarterSystemActor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISystemActorScope, SystemActorScope>();
        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;

namespace Starter.Integration.Tests.Support;

/// <summary>Wraps a host's registration of a service in a test decorator, keeping its lifetime.</summary>
public static class ServiceDecoration
{
    /// <summary>
    /// Replaces the last registration of <typeparamref name="TService"/> with one that builds the original (from its
    /// instance, factory or implementation type) and hands it to <paramref name="decorate"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">No (non-keyed) registration of <typeparamref name="TService"/> exists.</exception>
    public static IServiceCollection Decorate<TService>(this IServiceCollection services, Func<IServiceProvider, TService, TService> decorate)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(decorate);
        var original = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(TService) && !descriptor.IsKeyedService)
            ?? throw new InvalidOperationException($"{typeof(TService).Name} is not registered, so it cannot be decorated.");
        services.Remove(original);
        services.Add(ServiceDescriptor.Describe(
            typeof(TService),
            provider => decorate(provider, (TService)Build(provider, original)),
            original.Lifetime));
        return services;
    }

    private static object Build(IServiceProvider provider, ServiceDescriptor original) =>
        original.ImplementationInstance
        ?? original.ImplementationFactory?.Invoke(provider)
        ?? ActivatorUtilities.CreateInstance(provider, original.ImplementationType!);
}

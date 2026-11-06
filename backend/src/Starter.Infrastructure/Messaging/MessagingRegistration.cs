using Codelab.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Starter.Application.Messaging;
using Starter.Infrastructure.Persistence;

namespace Starter.Infrastructure.Messaging;

/// <summary>The kernel messaging registrations (called from <c>AddStarterKernel</c>).</summary>
public static class MessagingRegistration
{
    /// <summary>
    /// Registers <see cref="OutboxDbContext"/> on the application database (schema <c>messaging</c>, migrated on start
    /// with the module contexts when <c>Database:ApplyMigrationsOnStartup</c> is on), <see cref="IOutbox"/>, the drain
    /// (<see cref="IOutboxDrainRunner"/>, <see cref="OutboxDispatcher"/>, <see cref="IOutboxStore"/>) and
    /// <see cref="OutboxOptions"/> (<c>Messaging:Outbox</c>, on by default, validated on start). The drain is not
    /// scheduled here: the jobs module runs it. Requires <c>AddStarterPersistence</c> first.
    /// </summary>
    public static IServiceCollection AddStarterMessaging(this IServiceCollection services)
    {
        services.AddModuleDbContext<OutboxDbContext>(OutboxDbContext.SchemaName);
        services.TryAddScoped<IOutbox, EfOutbox>();
        services.AddOptions<OutboxOptions>().BindConfiguration(OutboxOptions.SectionName).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<OutboxOptions>, OutboxOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IFailureCodeResolver, DefaultFailureCodeResolver>();
        services.TryAddScoped<IOutboxStore, SqlOutboxStore>();
        services.TryAddScoped<OutboxDispatcher>();
        services.TryAddScoped<IOutboxDrainRunner, OutboxDrainRunner>();
        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="THandler"/> (scoped, resolved in a scope of its own per delivery) as the handler of
    /// its <see cref="IOutboxHandler.MessageType"/>. Registering the same handler twice is a no-op; two different
    /// handlers for one type fail the drain of that type.
    /// </summary>
    public static IServiceCollection AddOutboxHandler<THandler>(this IServiceCollection services)
        where THandler : class, IOutboxHandler
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOutboxHandler, THandler>());
        return services;
    }
}

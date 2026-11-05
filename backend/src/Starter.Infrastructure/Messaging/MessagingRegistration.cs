using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Starter.Application.Messaging;
using Starter.Infrastructure.Persistence;

namespace Starter.Infrastructure.Messaging;

/// <summary>The kernel messaging registrations (called from <c>AddStarterKernel</c>).</summary>
public static class MessagingRegistration
{
    /// <summary>
    /// Registers <see cref="OutboxDbContext"/> on the application database (schema <c>messaging</c>, migrated on start
    /// with the module contexts when <c>Database:ApplyMigrationsOnStartup</c> is on) and <see cref="IOutbox"/>.
    /// Requires <c>AddStarterPersistence</c> first.
    /// </summary>
    public static IServiceCollection AddStarterMessaging(this IServiceCollection services)
    {
        services.AddModuleDbContext<OutboxDbContext>(OutboxDbContext.SchemaName);
        services.TryAddScoped<IOutbox, EfOutbox>();
        return services;
    }
}

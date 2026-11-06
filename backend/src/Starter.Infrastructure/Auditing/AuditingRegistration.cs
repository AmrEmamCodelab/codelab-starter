using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Starter.Application.Auditing;
using Starter.Infrastructure.Persistence;

namespace Starter.Infrastructure.Auditing;

/// <summary>The kernel auditing registrations (called by <c>AddStarterPersistence</c>, so every host with a database audits).</summary>
public static class AuditingRegistration
{
    /// <summary>
    /// Registers <see cref="AuditDbContext"/> (schema <c>audit</c>, migrated on start with the module contexts when
    /// <c>Database:ApplyMigrationsOnStartup</c> is on), the scoped <see cref="AuditSaveChangesInterceptor"/> that
    /// <c>AddModuleDbContext</c> attaches to every module context, the SQL writer and <see cref="IAuditTrailQueries"/>.
    /// Needs <see cref="IAuditContextAccessor"/> and <see cref="TimeProvider"/> (the kernel registers both). Idempotent.
    /// </summary>
    public static IServiceCollection AddStarterAuditing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(AuditSaveChangesInterceptor)))
        {
            return services;
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddModuleDbContext<AuditDbContext>(AuditDbContext.SchemaName);
        services.TryAddSingleton<IAuditTrailWriter, SqlAuditTrailWriter>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.TryAddScoped<IAuditTrailQueries, EfAuditTrailQueries>();
        return services;
    }
}

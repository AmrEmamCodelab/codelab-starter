using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Starter.Application.Authorization;
using Starter.Application.Validation;
using Starter.Modules.Audit.Application;

namespace Starter.Modules.Audit.Infrastructure;

/// <summary>The Audit module's registrations (called by <see cref="AuditModule"/>).</summary>
public static class AuditModuleRegistration
{
    /// <summary>
    /// Registers the permission catalog source, the filter validator and the viewer use case. The trail itself
    /// (<c>IAuditTrailQueries</c>, the interceptor, <c>audit.AuditEntries</c>) is registered by the kernel.
    /// </summary>
    public static IServiceCollection AddAuditModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, AuditPermissionCatalogSource>());
        services.AddSingleton<IValidator<AuditTrailListQuery>, AuditTrailListQueryValidator>();
        services.AddScoped<ListAuditTrail>();
        // scaffold:registrations
        return services;
    }

    /// <summary>Maps nothing extra: the viewer is a controller, found by the kernel.</summary>
    public static IEndpointRouteBuilder UseAuditModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints;
    }
}

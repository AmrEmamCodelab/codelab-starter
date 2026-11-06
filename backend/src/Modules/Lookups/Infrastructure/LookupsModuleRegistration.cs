using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Starter.Application.Authorization;
using Starter.Application.Lookups;
using Starter.Application.Validation;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Lookups.Application;
using Starter.Modules.Lookups.Infrastructure.Caching;
using Starter.Modules.Lookups.Infrastructure.Persistence;

namespace Starter.Modules.Lookups.Infrastructure;

/// <summary>The Lookups module's registrations (called by <see cref="LookupsModule"/>).</summary>
public static class LookupsModuleRegistration
{
    /// <summary>
    /// Registers the permission catalog source, the DbContext in schema <c>lookups</c> and its repository, the
    /// per-node memory cache, the validator and the use cases. The module has no options: the cache TTL is the
    /// constant <see cref="LookupsLimits.CacheTtl"/>.
    /// </summary>
    public static IServiceCollection AddLookupsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, LookupsPermissionCatalogSource>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddModuleDbContext<LookupsDbContext>(LookupsDbContext.SchemaName);
        services.AddScoped<ILookupsUnitOfWork>(provider => provider.GetRequiredService<LookupsDbContext>());
        services.AddScoped<ILookupRepository, LookupRepository>();
        services.AddSingleton<ILookupCache, MemoryLookupCache>();
        services.AddSingleton<IValidator<SetLookupItemActiveCommand>, SetLookupItemActiveCommandValidator>();
        services.AddScoped<GetActiveLookupValues>();
        services.AddScoped<ILookupValues, ActiveLookupValues>();
        services.AddScoped<ListLookupTypes>();
        services.AddScoped<ListLookupItems>();
        services.AddScoped<SetLookupItemActive>();
        // scaffold:registrations
        return services;
    }

    /// <summary>Maps nothing extra: every Lookups endpoint is a controller, found by the kernel.</summary>
    public static IEndpointRouteBuilder UseLookupsModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints;
    }
}

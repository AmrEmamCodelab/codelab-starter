using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Starter.Infrastructure.Modularity;

/// <summary>Registers and maps the modules of a <see cref="ModuleCatalog"/>, so the host never names a module.</summary>
public static class ModuleRegistration
{
    /// <summary>
    /// Registers the catalog, runs every module's <see cref="IModule.AddModule"/>, and adds MVC controllers with the
    /// kernel assembly and every module assembly as application parts (a module's controllers live in its .Api
    /// namespace and are found without any registration).
    /// </summary>
    public static IServiceCollection AddStarterModules(this IServiceCollection services, ModuleCatalog catalog, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton(catalog);
        foreach (var module in catalog.Modules)
        {
            module.AddModule(services, configuration);
        }

        var mvc = services.AddControllers();
        mvc.ConfigureApplicationPartManager(parts => AddParts(parts, [typeof(ModuleRegistration).Assembly, .. catalog.Assemblies]));
        return services;
    }

    /// <summary>Maps the controllers, then every module's <see cref="IModule.UseModule"/> extras.</summary>
    public static IEndpointRouteBuilder MapStarterModules(this IEndpointRouteBuilder endpoints, ModuleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(catalog);
        endpoints.MapControllers();
        foreach (var module in catalog.Modules)
        {
            module.UseModule(endpoints);
        }

        return endpoints;
    }

    /// <summary>Adds one <see cref="AssemblyPart"/> per assembly that is not already a part.</summary>
    public static void AddParts(ApplicationPartManager manager, IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(assemblies);
        foreach (var assembly in assemblies.Distinct())
        {
            var alreadyPart = manager.ApplicationParts.OfType<AssemblyPart>().Any(part => part.Assembly == assembly);
            if (!alreadyPart)
            {
                manager.ApplicationParts.Add(new AssemblyPart(assembly));
            }
        }
    }
}

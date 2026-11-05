using System.Reflection;

namespace Starter.Infrastructure.Modularity;

/// <summary>
/// The modules a host references, discovered from its <see cref="ModuleAssemblyAttribute"/> list (generated from the
/// host's module ProjectReferences). Every listed assembly must be a module assembly of the same solution and hold
/// exactly one public <see cref="IModule"/> implementation with a parameterless constructor; module names are unique.
/// </summary>
public sealed class ModuleCatalog
{
    private ModuleCatalog(IReadOnlyList<IModule> modules) => Modules = modules;

    /// <summary>A catalog without modules (a host that references none).</summary>
    public static ModuleCatalog Empty { get; } = new([]);

    /// <summary>The modules, ordered by name.</summary>
    public IReadOnlyList<IModule> Modules { get; }

    /// <summary>The distinct module assemblies, in <see cref="Modules"/> order.</summary>
    public IReadOnlyList<Assembly> Assemblies => [.. Modules.Select(module => module.GetType().Assembly).Distinct()];

    /// <summary>Discovers the modules of <paramref name="hostAssembly"/>.</summary>
    /// <exception cref="InvalidOperationException">A listed assembly is not a module assembly or does not hold exactly one module.</exception>
    public static ModuleCatalog Discover(Assembly hostAssembly)
    {
        ArgumentNullException.ThrowIfNull(hostAssembly);
        var assemblies = hostAssembly.GetCustomAttributes<ModuleAssemblyAttribute>()
            .Select(attribute => attribute.AssemblyName)
            .Distinct(StringComparer.Ordinal)
            .Select(name => Assembly.Load(new AssemblyName(name)));
        return FromAssemblies(assemblies, hostAssembly);
    }

    /// <summary>Builds the catalog from explicit module assemblies (tests and tools).</summary>
    /// <exception cref="InvalidOperationException">An assembly is not a module assembly of the host's solution or does not hold exactly one module.</exception>
    public static ModuleCatalog FromAssemblies(IEnumerable<Assembly> moduleAssemblies, Assembly hostAssembly)
    {
        ArgumentNullException.ThrowIfNull(moduleAssemblies);
        ArgumentNullException.ThrowIfNull(hostAssembly);
        var modules = moduleAssemblies.Select(assembly => CreateModule(assembly, hostAssembly)).ToList();
        return FromModules(modules);
    }

    /// <summary>Builds the catalog from module instances (tests and tools).</summary>
    /// <exception cref="InvalidOperationException">Two modules share a name, or a name is blank.</exception>
    public static ModuleCatalog FromModules(IEnumerable<IModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        var list = modules.ToList();
        var unnamed = list.Where(module => string.IsNullOrWhiteSpace(module.Name)).Select(module => module.GetType().FullName).ToList();
        if (unnamed.Count > 0)
        {
            throw new InvalidOperationException($"Every module needs a name; {string.Join(", ", unnamed)} has none.");
        }

        var duplicates = list.GroupBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException($"Module names must be unique; registered more than once: {string.Join(", ", duplicates)}.");
        }

        return new ModuleCatalog([.. list.OrderBy(module => module.Name, StringComparer.Ordinal)]);
    }

    /// <summary>The one public, concrete <see cref="IModule"/> type of <paramref name="moduleAssembly"/>.</summary>
    /// <exception cref="InvalidOperationException">The assembly holds none, or more than one.</exception>
    public static Type ModuleTypeOf(Assembly moduleAssembly)
    {
        ArgumentNullException.ThrowIfNull(moduleAssembly);
        var moduleTypes = moduleAssembly.GetExportedTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IModule).IsAssignableFrom(type))
            .ToList();
        if (moduleTypes.Count != 1)
        {
            throw new InvalidOperationException(
                $"Module assembly '{moduleAssembly.GetName().Name}' must hold exactly one public {nameof(IModule)} implementation; found {moduleTypes.Count}.");
        }

        return moduleTypes[0];
    }

    private static IModule CreateModule(Assembly assembly, Assembly hostAssembly)
    {
        if (!ModuleNaming.IsModuleAssembly(assembly, hostAssembly))
        {
            throw new InvalidOperationException(
                $"'{assembly.GetName().Name}' is listed as a module but its name does not start with '{ModuleNaming.ModuleAssemblyPrefixOf(hostAssembly)}'.");
        }

        return (IModule)Activator.CreateInstance(ModuleTypeOf(assembly))!;
    }
}

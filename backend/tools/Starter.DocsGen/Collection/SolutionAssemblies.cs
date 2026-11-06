using System.Reflection;
using Starter.Infrastructure.Modularity;

namespace Starter.DocsGen.Collection;

/// <summary>
/// The assemblies DocsGen documents: the host, its modules (discovered like the host does, from its
/// <see cref="ModuleAssemblyAttribute"/> list) and everything they reference transitively that belongs to the host's
/// solution or is a Codelab package.
/// </summary>
internal static class SolutionAssemblies
{
    /// <summary>The name prefix of the platform packages (<c>Codelab.Security</c>, <c>Codelab.Errors</c>, ...).</summary>
    public const string PlatformPackagePrefix = "Codelab.";

    /// <summary>The documented assemblies, sorted by name.</summary>
    public static IReadOnlyList<Assembly> Discover(Assembly hostAssembly, ModuleCatalog modules)
    {
        ArgumentNullException.ThrowIfNull(hostAssembly);
        ArgumentNullException.ThrowIfNull(modules);
        var solutionPrefix = ModuleNaming.SolutionTokenOf(hostAssembly) + ".";
        var found = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>([hostAssembly, .. modules.Assemblies]);
        while (pending.TryDequeue(out var assembly))
        {
            var name = assembly.GetName().Name;
            if (name is null || !found.TryAdd(name, assembly))
            {
                continue;
            }

            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (IsDocumented(reference.Name, solutionPrefix) && !found.ContainsKey(reference.Name!))
                {
                    pending.Enqueue(Assembly.Load(reference));
                }
            }
        }

        return [.. found.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => entry.Value)];
    }

    /// <summary>
    /// The assemblies of <paramref name="assemblies"/> that belong to the host's own solution (the host, the kernel and
    /// the modules), without the Codelab packages; same order.
    /// </summary>
    public static IReadOnlyList<Assembly> OwnAssemblies(IEnumerable<Assembly> assemblies, Assembly hostAssembly)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(hostAssembly);
        var solutionPrefix = ModuleNaming.SolutionTokenOf(hostAssembly) + ".";
        return [.. assemblies.Where(assembly => assembly.GetName().Name?.StartsWith(solutionPrefix, StringComparison.Ordinal) == true)];
    }

    /// <summary>True for an assembly of the solution (<paramref name="solutionPrefix"/>) or a Codelab package.</summary>
    public static bool IsDocumented(string? assemblyName, string solutionPrefix) =>
        assemblyName is not null
        && (assemblyName.StartsWith(solutionPrefix, StringComparison.Ordinal)
            || assemblyName.StartsWith(PlatformPackagePrefix, StringComparison.Ordinal));
}

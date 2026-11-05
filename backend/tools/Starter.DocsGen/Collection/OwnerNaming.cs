using System.Reflection;
using Starter.Infrastructure.Modularity;

namespace Starter.DocsGen.Collection;

/// <summary>
/// Names the owner of a type for the docs: the module name for a module assembly, <see cref="KernelOwner"/> for any
/// other assembly of the host's solution, and the assembly name for a package (for example <c>Codelab.Security</c>).
/// Derived from assembly names only, so a fork's rename needs no change here (ADR-003).
/// </summary>
internal sealed class OwnerNaming(Assembly hostAssembly)
{
    /// <summary>The owner label of the solution's non-module assemblies.</summary>
    public const string KernelOwner = "Kernel";

    /// <summary>The owner label when an endpoint carries no handler method to trace.</summary>
    public const string UnknownOwner = "Platform";

    private readonly string solutionToken = ModuleNaming.SolutionTokenOf(hostAssembly);

    /// <summary>The owner of <paramref name="assembly"/>.</summary>
    public string OwnerOf(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (ModuleNaming.IsModuleAssembly(assembly, hostAssembly))
        {
            return ModuleNaming.NameOf(assembly);
        }

        return string.Equals(ModuleNaming.SolutionTokenOf(assembly), solutionToken, StringComparison.Ordinal)
            ? KernelOwner
            : assembly.GetName().Name ?? UnknownOwner;
    }

    /// <summary>The owner of <paramref name="type"/>'s assembly.</summary>
    public string OwnerOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return OwnerOf(type.Assembly);
    }
}

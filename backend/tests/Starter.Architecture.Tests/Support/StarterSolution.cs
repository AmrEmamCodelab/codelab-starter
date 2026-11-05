using System.Reflection;
using Codelab.Testing.Architecture;
using Starter.Application.Persistence;
using Starter.Domain.Exceptions;
using Starter.Infrastructure.Modularity;

namespace Starter.Architecture.Tests.Support;

/// <summary>
/// The solution under test, discovered rather than listed: the host assembly, the kernel assemblies and every module
/// the host references (its ModuleAssemblyAttributes). Removing a module from Starter.Api.csproj removes it here too.
/// </summary>
public static class StarterSolution
{
    /// <summary>The host (Starter.Api).</summary>
    public static Assembly Host { get; } = typeof(Program).Assembly;

    /// <summary>The kernel Domain assembly.</summary>
    public static Assembly KernelDomain { get; } = typeof(AccessDeniedException).Assembly;

    /// <summary>The kernel Application assembly (ports only).</summary>
    public static Assembly KernelApplication { get; } = typeof(IUnitOfWork).Assembly;

    /// <summary>The kernel Infrastructure assembly.</summary>
    public static Assembly KernelInfrastructure { get; } = typeof(IModule).Assembly;

    /// <summary>The kernel assemblies, innermost first.</summary>
    public static IReadOnlyList<Assembly> Kernel => [KernelDomain, KernelApplication, KernelInfrastructure];

    /// <summary>The modules the host references, in discovery order.</summary>
    public static ModuleCatalog Modules { get; } = ModuleCatalog.Discover(Host);

    /// <summary>The module assemblies.</summary>
    public static IReadOnlyList<Assembly> ModuleAssemblies => Modules.Assemblies;

    /// <summary>Every starter assembly: host, kernel and modules.</summary>
    public static IReadOnlyList<Assembly> All => [Host, .. Kernel, .. ModuleAssemblies];

    /// <summary>The module names, for xUnit theories (one row per discovered module).</summary>
    public static TheoryData<string> ModuleNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var assembly in ModuleAssemblies)
            {
                data.Add(ModuleNaming.NameOf(assembly));
            }

            return data;
        }
    }

    /// <summary>The repository root (the folder holding rules/rules.compiled.json).</summary>
    public static string RepositoryRootPath => RepositoryRoot.Find(CompiledRules.RelativePath);

    /// <summary>The assembly of the module named <paramref name="moduleName"/>.</summary>
    public static Assembly ModuleAssembly(string moduleName) =>
        ModuleAssemblies.Single(assembly => string.Equals(ModuleNaming.NameOf(assembly), moduleName, StringComparison.Ordinal));

    /// <summary>The root namespace of a module: its assembly name (ADR-004: one project, layers are namespaces).</summary>
    public static string RootNamespaceOf(Assembly moduleAssembly) =>
        moduleAssembly.GetName().Name ?? throw new InvalidOperationException("A module assembly without a name cannot be checked.");
}

using System.Reflection;

namespace Starter.Infrastructure.Modularity;

/// <summary>
/// Names derived from assembly names, never from hand-built strings, so a fork's full rename (ADR-003) needs no code
/// change: <c>Starter.Modules.Identity</c> gives the solution token <c>Starter</c> and the module name
/// <c>Identity</c>; <c>Starter.Api</c> gives the service name <c>starter-api</c>.
/// </summary>
public static class ModuleNaming
{
    /// <summary>The namespace segment that every module assembly carries after the solution token.</summary>
    public const string ModulesSegment = "Modules";

    private const char Separator = '.';
    private const char ServiceNameSeparator = '-';

    /// <summary>The module name: the last segment of the module assembly's name.</summary>
    public static string NameOf(Assembly moduleAssembly)
    {
        ArgumentNullException.ThrowIfNull(moduleAssembly);
        var name = SimpleName(moduleAssembly);
        return name[(name.LastIndexOf(Separator) + 1)..];
    }

    /// <summary>The solution token: the first segment of an assembly name (<c>Starter</c> in the starter).</summary>
    public static string SolutionTokenOf(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var name = SimpleName(assembly);
        var separator = name.IndexOf(Separator, StringComparison.Ordinal);
        return separator < 0 ? name : name[..separator];
    }

    /// <summary>The prefix every module assembly name of this solution starts with (<c>Starter.Modules.</c>).</summary>
    public static string ModuleAssemblyPrefixOf(Assembly hostAssembly) =>
        string.Concat(SolutionTokenOf(hostAssembly), Separator.ToString(), ModulesSegment, Separator.ToString());

    /// <summary>True when <paramref name="assembly"/> is a module assembly of the solution that <paramref name="hostAssembly"/> belongs to.</summary>
    public static bool IsModuleAssembly(Assembly assembly, Assembly hostAssembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return SimpleName(assembly).StartsWith(ModuleAssemblyPrefixOf(hostAssembly), StringComparison.Ordinal);
    }

    /// <summary>The service name of a host: its assembly name in lower case with dots as dashes (<c>starter-api</c>).</summary>
    public static string ServiceNameOf(Assembly hostAssembly)
    {
        ArgumentNullException.ThrowIfNull(hostAssembly);
        return SimpleName(hostAssembly).Replace(Separator, ServiceNameSeparator).ToLowerInvariant();
    }

    private static string SimpleName(Assembly assembly) =>
        assembly.GetName().Name ?? throw new InvalidOperationException("An assembly without a simple name cannot be named.");
}

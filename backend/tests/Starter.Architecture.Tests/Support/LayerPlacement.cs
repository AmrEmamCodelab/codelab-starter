using System.Reflection;
using System.Runtime.CompilerServices;
using Codelab.Testing.Architecture;
using Codelab.Testing.Checks;

namespace Starter.Architecture.Tests.Support;

/// <summary>
/// ADR-004 layering by namespace: every type of a module sits in one of its layer namespaces (<c>.Domain</c>,
/// <c>.Application</c>, <c>.Infrastructure</c>, <c>.Api</c>), except the module's own <c>IModule</c> class, which may sit
/// at the module root (backend/src/Modules/README.md).
/// </summary>
public static class LayerPlacement
{
    /// <summary>The four layer segments of a module.</summary>
    public static IReadOnlyList<string> Layers { get; } =
        [LayerNames.Domain, LayerNames.Application, LayerNames.Infrastructure, LayerNames.Api];

    /// <summary>Checks the top-level, non-generated types of <paramref name="moduleAssembly"/>.</summary>
    /// <param name="moduleAssembly">The module assembly.</param>
    /// <param name="rootNamespace">The module root namespace.</param>
    /// <param name="moduleType">The module's IModule type, the one type allowed at the root.</param>
    public static CheckResult EveryTypeIsInALayer(Assembly moduleAssembly, string rootNamespace, Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(moduleAssembly);
        var types = OwnTypes(moduleAssembly, rootNamespace).ToList();
        var violations = types
            .Where(type => type != moduleType && !IsInLayer(type.Namespace, rootNamespace))
            .Select(type => $"{type.FullName} is outside the layer namespaces {string.Join(", ", Layers)}");
        return new CheckResult($"Every type of {rootNamespace} is in a layer", violations, types.Count);
    }

    /// <summary>True when <paramref name="typeNamespace"/> is a layer namespace of the module, or nested in one.</summary>
    public static bool IsInLayer(string? typeNamespace, string rootNamespace) =>
        typeNamespace is not null && Layers.Any(layer => IsSameOrNested(typeNamespace, $"{rootNamespace}.{layer}"));

    /// <summary>True when <paramref name="candidate"/> equals <paramref name="root"/> or is nested in it (<c>A.B</c> covers <c>A.B.C</c>, never <c>A.BC</c>).</summary>
    public static bool IsSameOrNested(string candidate, string root) =>
        string.Equals(candidate, root, StringComparison.Ordinal) || candidate.StartsWith(root + ".", StringComparison.Ordinal);

    private static IEnumerable<Type> OwnTypes(Assembly assembly, string rootNamespace) =>
        assembly.GetTypes().Where(type =>
            !type.IsNested
            && type.Namespace is not null
            && IsSameOrNested(type.Namespace, rootNamespace)
            && type.GetCustomAttribute<CompilerGeneratedAttribute>() is null);
}

using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;

namespace Codelab.Testing.Architecture;

/// <summary>
/// The one place that calls NetArchTest.Rules (ADR-011): swapping to the ArchUnitNET fallback touches this file only.
/// Namespaces are matched exactly or as a parent ("A.B" covers "A.B" and "A.B.C", never "A.BC").
/// </summary>
internal sealed class DependencyQuery(IReadOnlyCollection<Assembly> assemblies)
{
    private const string NamespaceSeparator = ".";

    /// <summary>The full names of the types in <paramref name="namespacePattern"/>.</summary>
    public IReadOnlyList<string> TypesIn(string namespacePattern) =>
        [.. Types.InAssemblies(assemblies).That().ResideInNamespaceMatching(namespacePattern).GetTypes().Select(type => type.FullName ?? type.Name)];

    /// <summary>
    /// The full names of the types in <paramref name="namespacePattern"/> that use any of <paramref name="forbidden"/>
    /// (namespaces or full type names, nested namespaces included).
    /// </summary>
    public IReadOnlyList<string> TypesUsing(string namespacePattern, IReadOnlyCollection<string> forbidden)
    {
        if (forbidden.Count == 0)
        {
            return [];
        }

        var result = Types.InAssemblies(assemblies)
            .That().ResideInNamespaceMatching(namespacePattern)
            .ShouldNot().HaveDependencyOnAny([.. forbidden])
            .GetResult();
        return result.IsSuccessful ? [] : [.. (result.FailingTypeNames ?? []).Order(StringComparer.Ordinal)];
    }

    /// <summary>A pattern matching <paramref name="rootNamespace"/> and every namespace nested in it.</summary>
    public static string ExactOrNested(string rootNamespace) => $"^{Regex.Escape(rootNamespace)}(?:{Regex.Escape(NamespaceSeparator)}|$)";

    /// <summary>
    /// A pattern matching <paramref name="rootNamespace"/> and its nested namespaces, except <paramref name="excluded"/>
    /// and the namespaces nested in it.
    /// </summary>
    public static string ExactOrNestedExcept(string rootNamespace, string excluded) =>
        $"^{Regex.Escape(rootNamespace)}(?!{Regex.Escape(NamespaceSeparator + excluded)}(?:{Regex.Escape(NamespaceSeparator)}|$))(?:{Regex.Escape(NamespaceSeparator)}|$)";

    /// <summary>True when one namespace is the other or contains it.</summary>
    public static bool Overlaps(string first, string second) =>
        IsWithin(first, second) || IsWithin(second, first);

    private static bool IsWithin(string candidate, string root) =>
        string.Equals(candidate, root, StringComparison.Ordinal)
        || candidate.StartsWith(root + NamespaceSeparator, StringComparison.Ordinal);
}

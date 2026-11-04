using System.Reflection;
using Codelab.Testing.Checks;

namespace Codelab.Testing.Architecture;

/// <summary>
/// Rule <c>base.module-boundaries</c>: a module's types reference another module only when it is in the module's
/// <c>mayDependOn</c>, and never one in <c>mustNotDependOn</c>. Driven by <c>rules/rules.compiled.json</c>.
/// </summary>
public static class ModuleBoundaryRules
{
    private const string Title = "Module boundaries (rules/rules.compiled.json)";

    /// <summary>
    /// Checks every mapped .NET module whose types are in <paramref name="assemblies"/>. Modules without types there
    /// are skipped; the result is vacuous (and fails) when no module had any.
    /// </summary>
    public static CheckResult Check(CompiledRules rules, ModuleNamespaceMap namespaces, IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(namespaces);
        ArgumentNullException.ThrowIfNull(assemblies);
        var query = new DependencyQuery([.. assemblies]);
        var violations = new List<string>();
        var examined = 0;
        foreach (var module in rules.DotnetModules)
        {
            examined += CheckModule(module, rules, namespaces, query, violations);
        }

        return new CheckResult(Title, violations, examined);
    }

    /// <summary>Loads the compiled rules and the project-based namespace map of the enclosing repository, then checks.</summary>
    public static CheckResult CheckRepository(IEnumerable<Assembly> assemblies, string? startDirectory = null)
    {
        var root = RepositoryRoot.Find(CompiledRules.RelativePath, startDirectory);
        var rules = CompiledRules.Load(Path.Combine(root, CompiledRules.RelativePath));
        return Check(rules, ModuleNamespaceMap.FromProjects(rules, root), assemblies);
    }

    private static int CheckModule(CompiledModule module, CompiledRules rules, ModuleNamespaceMap namespaces, DependencyQuery query, List<string> violations)
    {
        var moduleNamespace = namespaces.NamespaceOf(module.Module);
        if (moduleNamespace is null)
        {
            return 0;
        }

        var pattern = DependencyQuery.ExactOrNested(moduleNamespace);
        var types = query.TypesIn(pattern);
        if (types.Count == 0)
        {
            return 0;
        }

        foreach (var other in ForbiddenModules(module, rules, namespaces, moduleNamespace))
        {
            var reason = module.MustNotDependOn.Contains(other.Module, StringComparer.Ordinal) ? "listed in mustNotDependOn" : "not in mayDependOn";
            violations.AddRange(query.TypesUsing(pattern, [other.Namespace])
                .Select(type => $"{module.Module}: {type} depends on module {other.Module} ({reason})"));
        }

        return types.Count;
    }

    private static IEnumerable<(string Module, string Namespace)> ForbiddenModules(
        CompiledModule module, CompiledRules rules, ModuleNamespaceMap namespaces, string moduleNamespace)
    {
        foreach (var other in rules.DotnetModules)
        {
            var otherNamespace = namespaces.NamespaceOf(other.Module);
            var isAllowed = module.MayDependOn.Contains(other.Module, StringComparer.Ordinal);
            var isSelfOrNested = otherNamespace is null || DependencyQuery.Overlaps(moduleNamespace, otherNamespace);
            if (!isAllowed && !isSelfOrNested)
            {
                yield return (other.Module, otherNamespace!);
            }
        }
    }
}

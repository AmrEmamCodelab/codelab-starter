using System.Reflection;
using Codelab.Testing.Checks;

namespace Codelab.Testing.Architecture;

/// <summary>
/// The layer rules inside one module (ADR-004 namespaces <c>.Domain</c>, <c>.Application</c>, <c>.Infrastructure</c>,
/// <c>.Api</c>): Domain is innermost, Application depends on ports only (rule <c>base.application-ports-only</c>), and
/// the Api layer never touches a DbContext (rule <c>base.no-dbcontext-in-api</c>; <c>.Api.Composition</c> is the
/// composition root and is exempt).
/// </summary>
public static class LayerRules
{
    /// <summary>The persistence namespace Application and Api must not use.</summary>
    public const string EntityFrameworkNamespace = "Microsoft.EntityFrameworkCore";

    /// <summary>The ASP.NET Core namespace Domain must not use.</summary>
    public const string AspNetCoreNamespace = "Microsoft.AspNetCore";

    /// <summary>The full name of EF Core's DbContext; every type deriving from it is a DbContext.</summary>
    public const string DbContextTypeName = "Microsoft.EntityFrameworkCore.DbContext";

    /// <summary>All three layer rules for the module rooted at <paramref name="moduleNamespace"/>.</summary>
    public static CheckResult Check(IEnumerable<Assembly> assemblies, string moduleNamespace)
    {
        var all = Materialize(assemblies);
        return CheckResult.Combine(
            $"Layer rules of {moduleNamespace}",
            [DomainIsInnermost(all, moduleNamespace), ApplicationUsesPortsOnly(all, moduleNamespace), ApiHasNoDbContext(all, moduleNamespace)]);
    }

    /// <summary>Domain types use no other layer of the module, no EF Core and no ASP.NET Core.</summary>
    public static CheckResult DomainIsInnermost(IEnumerable<Assembly> assemblies, string moduleNamespace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleNamespace);
        string[] forbidden =
        [
            Layer(moduleNamespace, LayerNames.Application), Layer(moduleNamespace, LayerNames.Infrastructure),
            Layer(moduleNamespace, LayerNames.Api), EntityFrameworkNamespace, AspNetCoreNamespace,
        ];
        return LayerCheck(assemblies, $"{LayerNames.Domain} is innermost", DependencyQuery.ExactOrNested(Layer(moduleNamespace, LayerNames.Domain)), forbidden);
    }

    /// <summary>Application types use neither Infrastructure, Api, EF Core nor any DbContext type.</summary>
    public static CheckResult ApplicationUsesPortsOnly(IEnumerable<Assembly> assemblies, string moduleNamespace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleNamespace);
        var all = Materialize(assemblies);
        string[] forbidden =
        [
            Layer(moduleNamespace, LayerNames.Infrastructure), Layer(moduleNamespace, LayerNames.Api), EntityFrameworkNamespace,
            .. DbContextTypes(all),
        ];
        return LayerCheck(all, $"{LayerNames.Application} depends on ports only", DependencyQuery.ExactOrNested(Layer(moduleNamespace, LayerNames.Application)), forbidden);
    }

    /// <summary>Api types (except <c>.Api.Composition</c>) use neither EF Core nor any DbContext type.</summary>
    public static CheckResult ApiHasNoDbContext(IEnumerable<Assembly> assemblies, string moduleNamespace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleNamespace);
        var all = Materialize(assemblies);
        string[] forbidden = [EntityFrameworkNamespace, .. DbContextTypes(all)];
        var pattern = DependencyQuery.ExactOrNestedExcept(Layer(moduleNamespace, LayerNames.Api), LayerNames.Composition);
        return LayerCheck(all, $"{LayerNames.Api} has no DbContext", pattern, forbidden);
    }

    /// <summary>The full names of every type in <paramref name="assemblies"/> that derives from EF Core's DbContext.</summary>
    public static IReadOnlyList<string> DbContextTypes(IEnumerable<Assembly> assemblies) =>
        [.. Materialize(assemblies).SelectMany(LoadableTypes).Where(DerivesFromDbContext).Select(type => type.FullName!).Order(StringComparer.Ordinal)];

    private static CheckResult LayerCheck(IEnumerable<Assembly> assemblies, string title, string pattern, IReadOnlyCollection<string> forbidden)
    {
        var query = new DependencyQuery(Materialize(assemblies));
        var examined = query.TypesIn(pattern).Count;
        var violations = query.TypesUsing(pattern, forbidden).Select(type => $"{type} uses a forbidden layer or type");
        return new CheckResult(title, violations, examined);
    }

    private static string Layer(string moduleNamespace, string layer) => $"{moduleNamespace}.{layer}";

    private static bool DerivesFromDbContext(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.FullName, DbContextTypeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    private static IReadOnlyCollection<Assembly> Materialize(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        return assemblies as IReadOnlyCollection<Assembly> ?? [.. assemblies];
    }
}

/// <summary>The layer namespace segments of a module (ADR-004).</summary>
public static class LayerNames
{
    /// <summary>Entities, value objects, domain rules and ports.</summary>
    public const string Domain = "Domain";

    /// <summary>Use cases; depends on ports only.</summary>
    public const string Application = "Application";

    /// <summary>EF Core, outbox and other adapters implementing the ports.</summary>
    public const string Infrastructure = "Infrastructure";

    /// <summary>Endpoints and controllers.</summary>
    public const string Api = "Api";

    /// <summary>The composition root inside the Api layer, the only Api code that may register a DbContext.</summary>
    public const string Composition = "Composition";
}

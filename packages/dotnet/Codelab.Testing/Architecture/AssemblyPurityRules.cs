using System.Reflection;
using Codelab.Testing.Checks;

namespace Codelab.Testing.Architecture;

/// <summary>
/// Keeps pure code pure: an assembly references no hosting, configuration or persistence assembly, and no public
/// signature in a namespace mentions such a type (generic arguments included). A port that takes an
/// <c>IOptions&lt;T&gt;</c> or an <c>HttpRequest</c> stops being a pure function of its inputs.
/// </summary>
public static class AssemblyPurityRules
{
    private const BindingFlags DeclaredPublic =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>The prefixes of hosting, configuration and persistence assemblies and namespaces.</summary>
    public static IReadOnlyList<string> HostingAndPersistencePrefixes { get; } =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Extensions.Configuration",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Hosting",
        "System.Configuration",
    ];

    /// <summary>The assembly references none of the assemblies whose name starts with a forbidden prefix.</summary>
    public static CheckResult ReferencesNone(Assembly assembly, IEnumerable<string> forbiddenPrefixes)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var prefixes = Prefixes(forbiddenPrefixes);
        var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty).ToList();
        var offenders = references.Where(name => StartsWithAny(name, prefixes)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return new CheckResult($"{assembly.GetName().Name} references no forbidden assembly", offenders.Select(name => $"references {name}"), references.Count);
    }

    /// <summary>No public member of a type in <paramref name="namespaceName"/> mentions a type with a forbidden prefix.</summary>
    public static CheckResult SignaturesMentionNone(Assembly assembly, string namespaceName, IEnumerable<string> forbiddenPrefixes)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);
        var prefixes = Prefixes(forbiddenPrefixes);
        var types = assembly.GetExportedTypes().Where(type => string.Equals(type.Namespace, namespaceName, StringComparison.Ordinal)).ToList();
        var violations = types
            .SelectMany(type => MentionedTypes(type).Select(name => (type, name)))
            .Where(entry => StartsWithAny(entry.name, prefixes))
            .Select(entry => $"{entry.type.Name} -> {entry.name}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        return new CheckResult($"Public signatures in {namespaceName} mention no forbidden type", violations, types.Count);
    }

    private static IEnumerable<string> MentionedTypes(Type type)
    {
        var constructorTypes = type.GetConstructors(DeclaredPublic).SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType);
        var propertyTypes = type.GetProperties(DeclaredPublic).Select(property => property.PropertyType);
        var methodTypes = type.GetMethods(DeclaredPublic)
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType));
        return constructorTypes.Concat(propertyTypes).Concat(methodTypes)
            .SelectMany(Flatten)
            .Select(mentioned => mentioned.FullName ?? $"{mentioned.Namespace}.{mentioned.Name}");
    }

    // Generic arguments count too: an IOptions<T> hides inside Task<>.
    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        var element = type.HasElementType ? type.GetElementType() : null;
        var nested = type.IsGenericType ? type.GetGenericArguments() : element is null ? [] : [element];
        foreach (var argument in nested.SelectMany(Flatten))
        {
            yield return argument;
        }
    }

    private static List<string> Prefixes(IEnumerable<string> forbiddenPrefixes)
    {
        ArgumentNullException.ThrowIfNull(forbiddenPrefixes);
        return [.. forbiddenPrefixes];
    }

    private static bool StartsWithAny(string name, List<string> prefixes) =>
        prefixes.Exists(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
}

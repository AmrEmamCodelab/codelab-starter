using System.Reflection;
using Starter.Application.Authorization;

namespace Starter.DocsGen.Collection;

/// <summary>
/// Reads the code conventions DocsGen documents by reflection: the permission catalog sources and every public static
/// <c>*ErrorCodes</c> class with its <c>const string</c> fields.
/// </summary>
internal static class ConventionScanner
{
    /// <summary>The suffix of every error codes class (<c>&lt;Name&gt;ErrorCodes</c>, <c>PlatformErrorCodes</c>).</summary>
    public const string ErrorCodesSuffix = "ErrorCodes";

    /// <summary>The catalog rows of <paramref name="sources"/>, owned by each source's assembly, sorted by key.</summary>
    public static IReadOnlyList<PermissionDoc> Permissions(IEnumerable<IPermissionCatalogSource> sources, OwnerNaming owners)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(owners);
        return [.. sources
            .SelectMany(source => source.Permissions.Select(permission =>
                new PermissionDoc(permission.Key, owners.OwnerOf(source.GetType()), permission.Description)))
            .OrderBy(permission => permission.Key, StringComparer.Ordinal)
            .ThenBy(permission => permission.Module, StringComparer.Ordinal)];
    }

    /// <summary>Every error code constant of <paramref name="assemblies"/>, sorted by owner, then code.</summary>
    public static IReadOnlyList<ErrorCodeDoc> ErrorCodes(IEnumerable<Assembly> assemblies, OwnerNaming owners, XmlDocumentation documentation)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(documentation);
        return [.. assemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(IsErrorCodesClass)
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
                .Select(field => new ErrorCodeDoc(
                    Code: (string)field.GetRawConstantValue()!,
                    Constant: type.Name + "." + field.Name,
                    Owner: owners.OwnerOf(type),
                    Description: documentation.SummaryOf(field))))
            .OrderBy(code => code.Owner, StringComparer.Ordinal)
            .ThenBy(code => code.Code, StringComparer.Ordinal)
            .ThenBy(code => code.Constant, StringComparer.Ordinal)];
    }

    /// <summary>True for a public static class whose name ends with <see cref="ErrorCodesSuffix"/>.</summary>
    public static bool IsErrorCodesClass(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type is { IsClass: true, IsAbstract: true, IsSealed: true }
            && type.Name.EndsWith(ErrorCodesSuffix, StringComparison.Ordinal);
    }
}

using System.Globalization;
using System.Reflection;

namespace Starter.DocsGen.Collection;

/// <summary>
/// Lists the configuration keys of every options class that declares <c>public const string SectionName</c>
/// (contract section 4). Properties are walked the way the configuration binder fills them: scalars become keys,
/// collections add <see cref="ArrayIndexSegment"/>, dictionaries add <see cref="DictionaryKeySegment"/>, nested
/// classes recurse. An options class that is itself a dictionary, or whose only bindable member is a get-only
/// collection, is bound at the section root (for example <c>Features:&lt;name&gt;</c>, <c>Identity:DevUsers:&lt;n&gt;</c>).
/// </summary>
internal static class ConfigKeyScanner
{
    /// <summary>The convention field every options class declares.</summary>
    public const string SectionNameField = "SectionName";

    /// <summary>The key segment of an array index.</summary>
    public const string ArrayIndexSegment = "<n>";

    /// <summary>The key segment of a dictionary key.</summary>
    public const string DictionaryKeySegment = "<name>";

    /// <summary>The configuration key separator.</summary>
    public const string KeySeparator = ":";

    private const int MaxNestingDepth = 4;

    /// <summary>Every key of every options class in <paramref name="assemblies"/>, sorted by owner, section, then key.</summary>
    public static IReadOnlyList<ConfigKeyDoc> Scan(IEnumerable<Assembly> assemblies, OwnerNaming owners, XmlDocumentation documentation)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(documentation);
        var rows = new List<ConfigKeyDoc>();
        foreach (var type in assemblies.SelectMany(assembly => assembly.GetExportedTypes()))
        {
            if (SectionNameOf(type) is { } section)
            {
                ScanOptions(type, new ScanContext(owners.OwnerOf(type), section, documentation, rows));
            }
        }

        return [.. rows
            .DistinctBy(row => row.Key, StringComparer.Ordinal)
            .OrderBy(row => row.Owner, StringComparer.Ordinal)
            .ThenBy(row => row.Section, StringComparer.Ordinal)
            .ThenBy(row => row.Key, StringComparer.Ordinal)];
    }

    /// <summary>The <c>SectionName</c> of a public, instantiable class, or null when it declares none.</summary>
    public static string? SectionNameOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type is not { IsClass: true, IsAbstract: false })
        {
            return null;
        }

        var field = type.GetField(SectionNameField, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        return field is { IsLiteral: true } && field.FieldType == typeof(string) ? (string?)field.GetRawConstantValue() : null;
    }

    private static void ScanOptions(Type type, ScanContext context)
    {
        if (TypeShapes.DictionaryValueTypeOf(type) is { } valueType)
        {
            AddElement(valueType, context.Section + KeySeparator + DictionaryKeySegment, context.Documentation.SummaryOf(type), depth: 0, context);
            return;
        }

        var properties = BindableProperties(type);
        if (properties is [{ SetMethod: null } only] && !TypeShapes.IsScalar(only.PropertyType))
        {
            ScanMember(only, context.Section, value: null, depth: 0, context);
            return;
        }

        ScanObject(type, context.Section, depth: 0, context);
    }

    private static void ScanObject(Type type, string prefix, int depth, ScanContext context)
    {
        var instance = TypeShapes.TryCreate(type);
        foreach (var property in BindableProperties(type))
        {
            var value = instance is null ? null : property.GetValue(instance);
            ScanMember(property, prefix + KeySeparator + property.Name, value, depth, context);
        }
    }

    private static void ScanMember(PropertyInfo property, string key, object? value, int depth, ScanContext context)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var description = context.Documentation.SummaryOf(property);
        if (TypeShapes.IsScalar(type))
        {
            context.Add(key, TypeShapes.Describe(type), TypeShapes.FormatDefault(value), description);
        }
        else if (TypeShapes.DictionaryValueTypeOf(type) is { } valueType)
        {
            AddElement(valueType, key + KeySeparator + DictionaryKeySegment, description, depth, context);
        }
        else if (TypeShapes.ElementTypeOf(type) is { } elementType)
        {
            AddElement(elementType, key + KeySeparator + ArrayIndexSegment, description, depth, context);
        }
        else if (depth < MaxNestingDepth)
        {
            ScanObject(type, key, depth + 1, context);
        }
    }

    private static void AddElement(Type elementType, string key, string description, int depth, ScanContext context)
    {
        var type = Nullable.GetUnderlyingType(elementType) ?? elementType;
        if (TypeShapes.IsScalar(type))
        {
            context.Add(key, TypeShapes.Describe(type), defaultValue: string.Empty, description);
        }
        else if (depth < MaxNestingDepth)
        {
            ScanObject(type, key, depth + 1, context);
        }
    }

    private static List<PropertyInfo> BindableProperties(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is { IsPublic: true } && property.GetIndexParameters().Length == 0)
            .Where(property => property.SetMethod is { IsPublic: true } || !TypeShapes.IsScalar(property.PropertyType))
            .OrderBy(property => property.Name, StringComparer.Ordinal)];

    private sealed record ScanContext(string Owner, string Section, XmlDocumentation Documentation, List<ConfigKeyDoc> Rows)
    {
        public void Add(string key, string type, string defaultValue, string description) =>
            Rows.Add(new ConfigKeyDoc(key, type, Owner, Section, defaultValue, description));
    }
}

/// <summary>How the configuration binder sees a type: scalar, collection, dictionary or object.</summary>
internal static class TypeShapes
{
    private static readonly Dictionary<Type, string> ScalarNames = new()
    {
        [typeof(bool)] = "bool",
        [typeof(int)] = "int",
        [typeof(long)] = "long",
        [typeof(short)] = "short",
        [typeof(byte)] = "byte",
        [typeof(double)] = "double",
        [typeof(float)] = "float",
        [typeof(decimal)] = "decimal",
        [typeof(string)] = "string",
        [typeof(TimeSpan)] = "TimeSpan",
        [typeof(DateTimeOffset)] = "DateTimeOffset",
        [typeof(Guid)] = "Guid",
        [typeof(Uri)] = "Uri",
    };

    /// <summary>True for a value the binder converts from one string: the known scalars and enums (nullable or not).</summary>
    public static bool IsScalar(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsEnum || ScalarNames.ContainsKey(underlying);
    }

    /// <summary>A readable type name; an enum lists its members.</summary>
    public static string Describe(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying.IsEnum)
        {
            return $"enum {underlying.Name} ({string.Join(", ", Enum.GetNames(underlying))})";
        }

        return ScalarNames.TryGetValue(underlying, out var name) ? name : underlying.Name;
    }

    /// <summary>The default as text: invariant culture, lower-case booleans, empty for null or an empty string.</summary>
    public static string FormatDefault(object? value) => value switch
    {
        null => string.Empty,
        bool flag => flag ? "true" : "false",
        string text => text,
        Enum member => member.ToString(),
        IFormattable formattable => formattable.ToString(format: null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>The value type of a string-keyed dictionary, or null.</summary>
    public static Type? DictionaryValueTypeOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return GenericInterfacesOf(type)
            .Where(candidate => candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>) || candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
            .Select(candidate => candidate.GetGenericArguments())
            .Where(arguments => arguments[0] == typeof(string))
            .Select(arguments => arguments[^1])
            .FirstOrDefault();
    }

    /// <summary>The element type of an array or a generic enumerable other than a string, or null.</summary>
    public static Type? ElementTypeOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type == typeof(string))
        {
            return null;
        }

        return type.IsArray
            ? type.GetElementType()
            : GenericInterfacesOf(type)
                .Where(candidate => candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .Select(candidate => candidate.GetGenericArguments()[0])
                .FirstOrDefault();
    }

    /// <summary>A fresh instance through the public parameterless constructor, or null when there is none.</summary>
    public static object? TryCreate(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.IsClass && !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) is not null
            ? Activator.CreateInstance(type)
            : null;
    }

    private static IEnumerable<Type> GenericInterfacesOf(Type type) =>
        (type.IsInterface ? type.GetInterfaces().Append(type) : type.GetInterfaces()).Where(candidate => candidate.IsGenericType);
}

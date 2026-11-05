using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Starter.DocsGen.Collection;

/// <summary>
/// Reads one-line summaries from the XML documentation file next to each assembly (every Codelab and starter project
/// builds one). A member without a file or a summary has the empty summary.
/// </summary>
internal sealed partial class XmlDocumentation
{
    private const string DocumentationExtension = ".xml";
    private const string MemberElement = "member";
    private const string NameAttribute = "name";
    private const string CrefAttribute = "cref";
    private const string LangwordAttribute = "langword";
    private const string SummaryElement = "summary";
    private const string CodeElement = "c";
    private const string TypePrefix = "T:";
    private const string FieldPrefix = "F:";
    private const string PropertyPrefix = "P:";
    private const string MethodPrefix = "M:";
    private const char NestedTypeSeparator = '+';
    private const char MemberSeparator = '.';
    private const char ParameterListStart = '(';
    private const char CrefKindSeparator = ':';
    private const string Backtick = "`";

    private readonly ConcurrentDictionary<Assembly, IReadOnlyDictionary<string, string>> summaries = new();

    /// <summary>The summary of <paramref name="type"/>.</summary>
    public string SummaryOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Lookup(type.Assembly, TypePrefix + DocumentationTypeName(type));
    }

    /// <summary>The summary of a field or a property; empty for any other member kind.</summary>
    public string SummaryOf(MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var owner = member.DeclaringType;
        if (owner is null)
        {
            return string.Empty;
        }

        return member switch
        {
            FieldInfo => Lookup(owner.Assembly, FieldPrefix + DocumentationTypeName(owner) + MemberSeparator + member.Name),
            PropertyInfo => Lookup(owner.Assembly, PropertyPrefix + DocumentationTypeName(owner) + MemberSeparator + member.Name),
            MethodInfo method => MethodSummary(method, owner),
            _ => string.Empty,
        };
    }

    /// <summary>Turns the inner XML of a summary into one plain line: <c>&lt;c&gt;x&lt;/c&gt;</c> and crefs become code spans.</summary>
    public static string Flatten(XElement summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var text = new StringBuilder();
        foreach (var node in summary.Nodes())
        {
            text.Append(FlattenNode(node));
        }

        return Whitespace().Replace(text.ToString(), " ").Trim();
    }

    /// <summary>Parses a documentation file's members into id to summary.</summary>
    public static IReadOnlyDictionary<string, string> Parse(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var members = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in document.Descendants(MemberElement))
        {
            var id = member.Attribute(NameAttribute)?.Value;
            var summary = member.Element(SummaryElement);
            if (id is not null && summary is not null)
            {
                members[id] = Flatten(summary);
            }
        }

        return members;
    }

    private string MethodSummary(MethodInfo method, Type owner)
    {
        var prefix = MethodPrefix + DocumentationTypeName(owner) + MemberSeparator + method.Name;
        var members = MembersOf(owner.Assembly);
        var matches = members.Where(entry => entry.Key == prefix || entry.Key.StartsWith(prefix + ParameterListStart, StringComparison.Ordinal))
            .Select(entry => entry.Value)
            .ToList();
        return matches.Count == 1 ? matches[0] : string.Empty;
    }

    private string Lookup(Assembly assembly, string id) =>
        MembersOf(assembly).TryGetValue(id, out var summary) ? summary : string.Empty;

    private IReadOnlyDictionary<string, string> MembersOf(Assembly assembly) =>
        summaries.GetOrAdd(assembly, Load);

    private static IReadOnlyDictionary<string, string> Load(Assembly assembly)
    {
        if (string.IsNullOrEmpty(assembly.Location))
        {
            return new Dictionary<string, string>();
        }

        var path = Path.ChangeExtension(assembly.Location, DocumentationExtension);
        return File.Exists(path) ? Parse(XDocument.Load(path)) : new Dictionary<string, string>();
    }

    private static string DocumentationTypeName(Type type)
    {
        var name = type.IsGenericType ? type.GetGenericTypeDefinition().FullName : type.FullName;
        return (name ?? type.Name).Replace(NestedTypeSeparator, MemberSeparator);
    }

    private static string FlattenNode(XNode node) => node switch
    {
        XText text => text.Value,
        XElement { Name.LocalName: CodeElement } code => Backtick + code.Value + Backtick,
        XElement element when element.Attribute(CrefAttribute) is { } cref => Backtick + ShortCref(cref.Value) + Backtick,
        XElement element when element.Attribute(LangwordAttribute) is { } langword => Backtick + langword.Value + Backtick,
        XElement element when element.Attribute(NameAttribute) is { } name && !element.Nodes().Any() => Backtick + name.Value + Backtick,
        XElement element => string.Concat(element.Nodes().Select(FlattenNode)),
        _ => string.Empty,
    };

    private static string ShortCref(string cref)
    {
        var withoutKind = cref[(cref.IndexOf(CrefKindSeparator, StringComparison.Ordinal) + 1)..];
        var parameters = withoutKind.IndexOf(ParameterListStart, StringComparison.Ordinal);
        var name = parameters < 0 ? withoutKind : withoutKind[..parameters];
        return name[(name.LastIndexOf(MemberSeparator) + 1)..];
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}

using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Starter.Infrastructure.Modularity;

namespace Starter.Architecture.Tests.Support;

/// <summary>
/// ADR-004 at the database: a module's SQL text names only its own schemas. Raw SQL that reads another module's tables
/// (<c>[lookups].[LookupItems]</c> from Portal) bypasses that module's port the way a project reference would. Every
/// string the code builds is folded with Roslyn (literals, <c>const</c> concatenations and interpolations, across files),
/// so splitting the name into constants does not hide it.
/// </summary>
public static class CrossSchemaSqlScanner
{
    /// <summary>The constant every schema owner declares (<c>public const string SchemaName</c>).</summary>
    public const string SchemaConstantName = "SchemaName";

    private const string Unknown = "\0";
    private const string CompilationName = "CrossSchemaScan";
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The schema of every <see cref="SchemaConstantName"/> constant in <paramref name="assemblies"/>, mapped to its
    /// owner: the module name for a module assembly, <see cref="SourceFiles.KernelOwner"/> otherwise.
    /// </summary>
    public static IReadOnlyDictionary<string, string> SchemaOwners(IReadOnlyList<Assembly> assemblies, IReadOnlyList<Assembly> moduleAssemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(moduleAssemblies);
        return assemblies
            .SelectMany(assembly => assembly.GetTypes().Select(type => (Assembly: assembly, Field: type.GetField(SchemaConstantName, BindingFlags.Public | BindingFlags.Static))))
            .Where(pair => pair.Field is { IsLiteral: true } field && field.FieldType == typeof(string))
            .Select(pair => (Schema: (string)pair.Field!.GetRawConstantValue()!, Owner: moduleAssemblies.Contains(pair.Assembly) ? ModuleNaming.NameOf(pair.Assembly) : SourceFiles.KernelOwner))
            .DistinctBy(pair => pair.Schema, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Schema, pair => pair.Owner, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every string in <paramref name="sources"/> that names a schema its file's owner does not own, as <c>file:line owner -> schema</c>.</summary>
    public static IReadOnlyList<string> Scan(IReadOnlyList<OwnedSource> sources, IReadOnlyDictionary<string, string> schemaOwners)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(schemaOwners);
        var trees = sources.Select(source => CSharpSyntaxTree.ParseText(source.Text, path: source.Name)).ToList();
        var compilation = CSharpCompilation.Create(
            CompilationName,
            trees,
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return [.. sources.Zip(trees).SelectMany(pair => ScanTree(pair.First, compilation.GetSemanticModel(pair.Second), schemaOwners))];
    }

    /// <summary>The pattern that finds <paramref name="schema"/> used as a SQL schema: <c>[schema].</c>, or <c>FROM schema.</c> and the like.</summary>
    public static Regex SchemaReference(string schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        var name = Regex.Escape(schema);
        return new Regex(
            $@"\[{name}\]\s*\.|\b(?:FROM|JOIN|INTO|UPDATE|TABLE|MERGE|EXEC|EXECUTE|REFERENCES)\s+{name}\s*\.",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            MatchTimeout);
    }

    private static IEnumerable<string> ScanTree(OwnedSource source, SemanticModel model, IReadOnlyDictionary<string, string> schemaOwners)
    {
        var foreign = schemaOwners
            .Where(pair => !string.Equals(pair.Value, source.Owner, StringComparison.Ordinal))
            .Select(pair => (Schema: pair.Key, Pattern: SchemaReference(pair.Key)))
            .ToList();
        foreach (var expression in model.SyntaxTree.GetRoot().DescendantNodes().OfType<ExpressionSyntax>().Where(IsOutermostString))
        {
            var text = Render(expression, model);
            foreach (var (schema, pattern) in foreign.Where(candidate => candidate.Pattern.IsMatch(text)))
            {
                yield return $"{source.Name}:{expression.GetLocation().GetLineSpan().StartLinePosition.Line + 1} {source.Owner} -> {schema}";
            }
        }
    }

    private static bool IsOutermostString(ExpressionSyntax expression) =>
        IsStringShaped(expression) && !(expression.Parent is ExpressionSyntax parent && IsStringShaped(parent));

    private static bool IsStringShaped(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax literal => literal.IsKind(SyntaxKind.StringLiteralExpression) || literal.IsKind(SyntaxKind.Utf8StringLiteralExpression),
        InterpolatedStringExpressionSyntax => true,
        BinaryExpressionSyntax binary => binary.IsKind(SyntaxKind.AddExpression),
        ParenthesizedExpressionSyntax parenthesized => IsStringShaped(parenthesized.Expression),
        _ => false,
    };

    private static string Render(ExpressionSyntax expression, SemanticModel model)
    {
        if (model.GetConstantValue(expression) is { HasValue: true, Value: var value })
        {
            return value?.ToString() ?? Unknown;
        }

        return expression switch
        {
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression) => Render(binary.Left, model) + Render(binary.Right, model),
            ParenthesizedExpressionSyntax parenthesized => Render(parenthesized.Expression, model),
            InterpolatedStringExpressionSyntax interpolated => RenderInterpolation(interpolated, model),
            _ => Unknown,
        };
    }

    private static string RenderInterpolation(InterpolatedStringExpressionSyntax interpolated, SemanticModel model)
    {
        var text = new StringBuilder();
        foreach (var content in interpolated.Contents)
        {
            text.Append(content switch
            {
                InterpolatedStringTextSyntax part => part.TextToken.ValueText,
                InterpolationSyntax hole => Render(hole.Expression, model),
                _ => Unknown,
            });
        }

        return text.ToString();
    }
}

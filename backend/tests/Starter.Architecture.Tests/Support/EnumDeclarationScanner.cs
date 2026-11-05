using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Starter.Architecture.Tests.Support;

/// <summary>One enum declaration found in source.</summary>
/// <param name="Location">Where it is declared (<c>file:line</c>).</param>
/// <param name="Name">The enum name.</param>
/// <param name="Problems">What breaks rule <c>base.enums-as-int</c>; empty when the declaration is fine.</param>
public sealed record EnumDeclarationFinding(string Location, string Name, IReadOnlyList<string> Problems);

/// <summary>
/// Rule <c>base.enums-as-int</c> at the source level, where reflection cannot see it: an enum declares no underlying
/// type other than <c>int</c>, and every member has an explicit value (<c>Approved = 3</c>), so reordering members never
/// changes stored or sent data.
/// </summary>
public static class EnumDeclarationScanner
{
    /// <summary>The C# source file pattern.</summary>
    public const string SourcePattern = "*.cs";

    private const string IntKeyword = "int";
    private const string Int32TypeName = "Int32";
    private static readonly string[] BuildOutputSegments = ["bin", "obj"];

    /// <summary>Every enum declared in <paramref name="source"/>.</summary>
    /// <param name="source">The C# source text.</param>
    /// <param name="sourceName">The name used in each finding's location.</param>
    public static IReadOnlyList<EnumDeclarationFinding> Scan(string source, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(source);
        var tree = CSharpSyntaxTree.ParseText(source);
        return [.. tree.GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>().Select(declaration => Describe(declaration, sourceName))];
    }

    /// <summary>Every enum declared in the C# files under <paramref name="directory"/> (build output excluded).</summary>
    public static IReadOnlyList<EnumDeclarationFinding> ScanDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return [.. SourceFiles(directory).SelectMany(path => Scan(File.ReadAllText(path), Path.GetRelativePath(directory, path)))];
    }

    private static IEnumerable<string> SourceFiles(string directory) =>
        Directory.EnumerateFiles(directory, SourcePattern, SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(directory, path)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => BuildOutputSegments.Contains(segment, StringComparer.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal);

    private static EnumDeclarationFinding Describe(EnumDeclarationSyntax declaration, string sourceName)
    {
        var problems = new List<string>();
        var underlying = declaration.BaseList?.Types.FirstOrDefault()?.Type.ToString();
        if (underlying is not null && !IsInt(underlying))
        {
            problems.Add($"underlying type is '{underlying}', not int");
        }

        problems.AddRange(declaration.Members
            .Where(member => member.EqualsValue is null)
            .Select(member => $"member '{member.Identifier.Text}' has no explicit value"));
        var line = declaration.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        return new EnumDeclarationFinding($"{sourceName}:{line}", declaration.Identifier.Text, problems);
    }

    private static bool IsInt(string typeName) =>
        string.Equals(typeName, IntKeyword, StringComparison.Ordinal)
        || typeName.EndsWith(Int32TypeName, StringComparison.Ordinal);
}

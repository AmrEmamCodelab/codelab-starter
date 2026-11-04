using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Codelab.Build.Analyzers.Tests.Harness;

/// <summary>Analyzer options backed by a dictionary (what an .editorconfig section or a globalconfig yields).</summary>
internal sealed class DictionaryOptions(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
{
    public static readonly DictionaryOptions Empty = new(new Dictionary<string, string>());

    public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) =>
        values.TryGetValue(key, out value);
}

/// <summary>Every tree gets the same tree options; the global options are separate, as in a real build.</summary>
internal sealed class DictionaryOptionsProvider(AnalyzerConfigOptions treeOptions, AnalyzerConfigOptions globalOptions)
    : AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions => globalOptions;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => treeOptions;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => treeOptions;
}

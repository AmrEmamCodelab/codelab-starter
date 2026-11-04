using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Codelab.Build.Analyzers.Options;

/// <summary>Per-compilation cache: the options of a tree are read once, however many nodes the analyzers visit.</summary>
internal sealed class ThresholdCache
{
    private readonly AnalyzerConfigOptionsProvider _provider;
    private readonly ConcurrentDictionary<SyntaxTree, CodelabThresholds> _byTree = new();

    public ThresholdCache(AnalyzerConfigOptionsProvider provider)
    {
        _provider = provider;
    }

    public CodelabThresholds For(SyntaxTree tree) =>
        _byTree.GetOrAdd(tree, key => ThresholdReader.Read(_provider.GetOptions(key), _provider.GlobalOptions));
}

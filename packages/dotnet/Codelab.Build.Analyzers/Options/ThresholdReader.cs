using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Codelab.Build.Analyzers.Options;

/// <summary>
/// Reads the codelab_* keys for a tree: the tree's .editorconfig options win (module overrides), then the global
/// options (rules/codelab.globalconfig, base defaults), then <see cref="DefaultThresholds"/>. A value that does not
/// parse falls back the same way, so a broken key never disables a gate.
/// </summary>
internal static class ThresholdReader
{
    private const char ListSeparator = ',';

    public static CodelabThresholds Read(AnalyzerConfigOptions treeOptions, AnalyzerConfigOptions globalOptions)
    {
        var lookup = new OptionLookup(treeOptions, globalOptions);
        var sizes = new SizeLimits(
            methodLines: lookup.PositiveInt(OptionKeys.MethodLines, DefaultThresholds.MethodLines),
            fileLines: lookup.PositiveInt(OptionKeys.FileLines, DefaultThresholds.FileLines),
            parameters: lookup.PositiveInt(OptionKeys.Parameters, DefaultThresholds.Parameters));
        var magicNumbers = new MagicNumberPolicy(
            allowedNumbers: lookup.NumberList(OptionKeys.AllowedNumbers, DefaultThresholds.AllowedNumbers),
            excludeTests: lookup.Boolean(OptionKeys.ExcludeTests, DefaultThresholds.ExcludeTests),
            enforced: lookup.Boolean(OptionKeys.MagicNumbersEnforced, DefaultThresholds.MagicNumbersEnforced));
        return new CodelabThresholds(sizes, magicNumbers);
    }

    /// <summary>Parses "-1, 0, 1" into numbers; null when any entry is not a number.</summary>
    public static ImmutableArray<double>? ParseNumberList(string text)
    {
        var builder = ImmutableArray.CreateBuilder<double>();
        foreach (var part in text.Split(ListSeparator))
        {
            if (!double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return null;
            }

            builder.Add(number);
        }

        return builder.ToImmutable();
    }

    private sealed class OptionLookup
    {
        private readonly AnalyzerConfigOptions _treeOptions;
        private readonly AnalyzerConfigOptions _globalOptions;

        public OptionLookup(AnalyzerConfigOptions treeOptions, AnalyzerConfigOptions globalOptions)
        {
            _treeOptions = treeOptions;
            _globalOptions = globalOptions;
        }

        public int PositiveInt(string key, int fallback) =>
            Raw(key) is { } text && int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
                ? value
                : fallback;

        public bool Boolean(string key, bool fallback) =>
            Raw(key) is { } text && bool.TryParse(text.Trim(), out var value) ? value : fallback;

        public ImmutableArray<double> NumberList(string key, ImmutableArray<double> fallback) =>
            Raw(key) is { } text && ParseNumberList(text) is { } numbers ? numbers : fallback;

        private string? Raw(string key)
        {
            if (_treeOptions.TryGetValue(key, out var treeValue))
            {
                return treeValue;
            }

            return _globalOptions.TryGetValue(key, out var globalValue) ? globalValue : null;
        }
    }
}

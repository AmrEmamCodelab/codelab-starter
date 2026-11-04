using Microsoft.Extensions.Configuration;

namespace Codelab.Observability;

/// <summary>
/// Splits the <see cref="SectionName"/> configuration section in two, which is the fix for the root
/// <c>ReadFrom.Configuration</c> defect of the harvested bootstrap.
/// <para>
/// <b>The defect.</b> The whole <c>Serilog</c> section was read on the ROOT logger, so a sink declared under
/// <c>Serilog:WriteTo</c> was attached beside the retained sinks instead of inside them: it skipped the host's
/// PII predicate (<see cref="ObservabilityOptions.IsUnsafeForRetainedSinks"/>) and the exception-text redaction.
/// </para>
/// <para>
/// <b>The fix.</b> The root reads only <see cref="RootSlice"/>: the levels (<c>MinimumLevel</c>, with its overrides,
/// which Serilog evaluates on the root only, and <c>LevelSwitches</c>), so the root logger still honours the
/// configured levels. Everything else (<c>Using</c>, <c>WriteTo</c>, <c>Enrich</c>, <c>Filter</c>, ...) is
/// <see cref="SinkSlice"/>, read inside the retained sub-logger, so a configured sink gets the same filter and
/// redaction as the file and Seq.
/// </para>
/// </summary>
internal static class SerilogConfigurationSlices
{
    /// <summary>The Serilog configuration section.</summary>
    public const string SectionName = "Serilog";

    private const string MinimumLevelKey = "MinimumLevel";
    private const string LevelSwitchesKey = "LevelSwitches";
    private const char KeySeparator = ':';

    private static readonly HashSet<string> RootKeys = new(StringComparer.OrdinalIgnoreCase) { MinimumLevelKey, LevelSwitchesKey };

    /// <summary>The level settings only, under <see cref="SectionName"/>.</summary>
    public static IConfiguration RootSlice(IConfiguration configuration) => Slice(configuration, keepRootKeys: true);

    /// <summary>Everything except the level settings, under <see cref="SectionName"/>.</summary>
    public static IConfiguration SinkSlice(IConfiguration configuration) => Slice(configuration, keepRootKeys: false);

    private static IConfiguration Slice(IConfiguration configuration, bool keepRootKeys)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);
        var values = section.AsEnumerable(makePathsRelative: true)
            .Where(pair => pair.Value is not null && RootKeys.Contains(FirstSegment(pair.Key)) == keepRootKeys)
            .Select(pair => KeyValuePair.Create(SectionName + KeySeparator + pair.Key, pair.Value));

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static string FirstSegment(string relativeKey)
    {
        var end = relativeKey.IndexOf(KeySeparator, StringComparison.Ordinal);
        return end < 0 ? relativeKey : relativeKey[..end];
    }
}

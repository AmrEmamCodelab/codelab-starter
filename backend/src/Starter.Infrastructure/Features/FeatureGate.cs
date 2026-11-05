using Microsoft.Extensions.Options;

namespace Starter.Infrastructure.Features;

/// <summary>Answers whether a declared feature flag is on in this deployment.</summary>
public interface IFeatureGate
{
    /// <summary>Every declared flag and its effective value, ordered by name.</summary>
    IReadOnlyDictionary<string, bool> Flags { get; }

    /// <summary>The effective value of <paramref name="flag"/>.</summary>
    /// <exception cref="InvalidOperationException">No module declares <paramref name="flag"/> (a programming fault).</exception>
    bool IsEnabled(string flag);
}

/// <summary>
/// The effective flags: every declared flag with its configured value, or its default when not configured. Built once
/// at startup; changing a switch is a configuration change plus a restart.
/// </summary>
public sealed class FeatureGate : IFeatureGate
{
    private readonly SortedDictionary<string, bool> _flags = new(StringComparer.Ordinal);

    /// <summary>Builds the gate from the declared flags and the configured switches.</summary>
    public FeatureGate(IEnumerable<IFeatureFlagSource> sources, IOptions<FeatureSwitchDictionary> switches)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(switches);
        foreach (var flag in sources.SelectMany(source => source.Flags))
        {
            _flags[flag.Name] = switches.Value.TryGetValue(flag.Name, out var configured) ? configured : flag.DefaultValue;
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, bool> Flags => _flags;

    /// <inheritdoc />
    public bool IsEnabled(string flag) =>
        _flags.TryGetValue(flag, out var value)
            ? value
            : throw new InvalidOperationException($"Feature flag '{flag}' is not declared by any module (add it to the module's {nameof(IFeatureFlagSource)}).");
}

/// <summary>
/// Start-up validation of the flag declarations and the <c>Features</c> section: declared names are camelCase and
/// unique, and every configured switch names a declared flag (a misspelled switch fails the start instead of silently
/// doing nothing).
/// </summary>
public sealed class FeatureSwitchValidator(IEnumerable<IFeatureFlagSource> sources) : IValidateOptions<FeatureSwitchDictionary>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, FeatureSwitchDictionary options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var declared = sources.SelectMany(source => source.Flags).Select(flag => flag.Name).ToList();
        var problems = new List<string>();
        problems.AddRange(declared.Where(flag => !IsCamelCase(flag)).Select(flag => $"Feature flag '{flag}' must be camelCase."));
        problems.AddRange(declared.GroupBy(flag => flag, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1)
            .Select(group => $"Feature flag '{group.Key}' is declared more than once."));
        var known = declared.ToHashSet(StringComparer.OrdinalIgnoreCase);
        problems.AddRange(options.Keys.Where(key => !known.Contains(key))
            .Select(key => $"{FeatureSwitchDictionary.SectionName}:{key} is not a declared feature flag."));
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    private static bool IsCamelCase(string flag) =>
        flag.Length > 0 && char.IsAsciiLetterLower(flag[0]) && flag.All(char.IsAsciiLetterOrDigit);
}

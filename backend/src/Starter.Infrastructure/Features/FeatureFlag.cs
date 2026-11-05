namespace Starter.Infrastructure.Features;

/// <summary>
/// One deployment-level switch: is a capability on for this deployment at all (the same for everyone). It is not an
/// authorization control; a gated endpoint still carries its own permission attribute.
/// </summary>
/// <param name="Name">The flag name in camelCase; the configuration key is <c>Features:&lt;Name&gt;</c>.</param>
/// <param name="DefaultValue">The value when the key is not configured.</param>
/// <param name="Description">What the flag switches, in English, for the generated docs.</param>
public sealed record FeatureFlag(string Name, bool DefaultValue, string Description);

/// <summary>A module's declared feature flags. A configured flag that no module declares refuses the host start.</summary>
public interface IFeatureFlagSource
{
    /// <summary>The flags this module declares.</summary>
    IReadOnlyCollection<FeatureFlag> Flags { get; }
}

/// <summary>The configured feature switches (section <c>Features</c>): flag name to on/off.</summary>
public sealed class FeatureSwitchDictionary : Dictionary<string, bool>
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Features";

    /// <summary>Creates an empty, case-insensitive switch map (configuration keys are case-insensitive).</summary>
    public FeatureSwitchDictionary()
        : base(StringComparer.OrdinalIgnoreCase)
    {
    }
}

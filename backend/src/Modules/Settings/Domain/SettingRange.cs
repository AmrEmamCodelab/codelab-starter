namespace Starter.Modules.Settings.Domain;

/// <summary>
/// The inclusive bounds of a number setting (an integer, or a duration in seconds). Either bound may be absent. Text and
/// boolean settings ignore the range.
/// </summary>
/// <param name="Min">The smallest accepted value, or null for no lower bound.</param>
/// <param name="Max">The largest accepted value, or null for no upper bound.</param>
public sealed record SettingRange(int? Min, int? Max)
{
    /// <summary>No bound at all.</summary>
    public static SettingRange None { get; } = new(Min: null, Max: null);

    /// <summary>True when <paramref name="value"/> is within both bounds.</summary>
    public bool Contains(int value) => (Min is not { } min || value >= min) && (Max is not { } max || value <= max);
}

namespace Starter.Modules.Settings.Domain;

/// <summary>Why a value was refused for a setting (see <see cref="SettingValueFormat.Check"/>).</summary>
public enum SettingValueProblem
{
    /// <summary>The value is acceptable.</summary>
    None = 0,

    /// <summary>The value does not have the shape of the setting's type (not a number, not true/false, ...).</summary>
    InvalidFormat = 1,

    /// <summary>The number is outside the setting's <see cref="SettingRange"/>.</summary>
    OutOfRange = 2,

    /// <summary>The value is longer than <see cref="SettingFieldLimits.ValueMaxLength"/>.</summary>
    TooLong = 3,
}

/// <summary>The result of checking a value against a setting's type and range.</summary>
/// <param name="Problem">Why it was refused, or <see cref="SettingValueProblem.None"/>.</param>
/// <param name="CanonicalValue">The value as it is stored (trimmed, invariant culture, lower-case boolean), or null when refused.</param>
public sealed record SettingValueCheck(SettingValueProblem Problem, string? CanonicalValue)
{
    /// <summary>True when the value is acceptable and <see cref="CanonicalValue"/> is set.</summary>
    public bool IsValid => Problem is SettingValueProblem.None;

    /// <summary>An accepted value.</summary>
    public static SettingValueCheck Accepted(string canonicalValue) => new(SettingValueProblem.None, canonicalValue);

    /// <summary>A refused value.</summary>
    public static SettingValueCheck Refused(SettingValueProblem problem) => new(problem, CanonicalValue: null);
}

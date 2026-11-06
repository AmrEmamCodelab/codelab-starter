using Starter.Application.Settings;

namespace Starter.Modules.Settings.Domain;

/// <summary>The definition of one runtime setting: what a migration inserts. The value starts at the default.</summary>
/// <param name="Key">The unique key (<c>&lt;group&gt;.&lt;kebab-name&gt;</c>, from a <c>SettingKeys</c> class).</param>
/// <param name="Group">The group the admin screen lists it under (the part of the key before the first dot).</param>
/// <param name="ValueType">How the value is read and validated.</param>
/// <param name="DefaultValue">The canonical default value (also the first value).</param>
/// <param name="DescriptionAr">What it controls, in Arabic.</param>
/// <param name="DescriptionEn">What it controls, in English.</param>
/// <param name="Range">The bounds of a number setting (<see cref="SettingRange.None"/> for text and boolean).</param>
public sealed record SettingDefinition(
    string Key,
    string Group,
    SettingValueType ValueType,
    string DefaultValue,
    string DescriptionAr,
    string DescriptionEn,
    SettingRange Range);

/// <summary>
/// A runtime setting an administrator can change without a deployment (<c>settings.Settings</c>). Settings are created
/// and removed only by migrations; the admin screen changes the value only. Every change advances the
/// <see cref="SettingsStamp"/> in the same transaction, so every node notices it.
/// </summary>
public sealed class Setting
{
    private Setting()
    {
    }

    /// <summary>The unique key (primary key).</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>The group the admin screen lists it under.</summary>
    public string Group { get; private set; } = string.Empty;

    /// <summary>How the value is read and validated (stored as the integer value).</summary>
    public SettingValueType ValueType { get; private set; }

    /// <summary>The current value, in canonical form (<see cref="SettingValueFormat"/>).</summary>
    public string Value { get; private set; } = string.Empty;

    /// <summary>The value the migration seeded.</summary>
    public string DefaultValue { get; private set; } = string.Empty;

    /// <summary>What it controls, in Arabic.</summary>
    public string DescriptionAr { get; private set; } = string.Empty;

    /// <summary>What it controls, in English.</summary>
    public string DescriptionEn { get; private set; } = string.Empty;

    /// <summary>The smallest accepted number, or null.</summary>
    public int? MinValue { get; private set; }

    /// <summary>The largest accepted number, or null.</summary>
    public int? MaxValue { get; private set; }

    /// <summary>When an administrator last changed it, or null when it still holds the seeded value.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>The internal id of the user who last changed it, or null.</summary>
    public long? UpdatedByUserId { get; private set; }

    /// <summary>The bounds of a number setting.</summary>
    public SettingRange Range => new(MinValue, MaxValue);

    /// <summary>A setting holding its default value (the shape a migration inserts; also used by tests).</summary>
    /// <exception cref="ArgumentException">The default value does not fit the type or the range.</exception>
    public static Setting Define(SettingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var check = SettingValueFormat.Check(definition.ValueType, definition.DefaultValue, definition.Range);
        if (!check.IsValid)
        {
            throw new ArgumentException($"The default of setting '{definition.Key}' is invalid ({check.Problem}).", nameof(definition));
        }

        return new Setting
        {
            Key = definition.Key,
            Group = definition.Group,
            ValueType = definition.ValueType,
            Value = check.CanonicalValue!,
            DefaultValue = check.CanonicalValue!,
            DescriptionAr = definition.DescriptionAr,
            DescriptionEn = definition.DescriptionEn,
            MinValue = definition.Range.Min,
            MaxValue = definition.Range.Max,
        };
    }

    /// <summary>Checks <paramref name="value"/> against this setting's type and range.</summary>
    public SettingValueCheck Check(string value) => SettingValueFormat.Check(ValueType, value, Range);

    /// <summary>
    /// Sets a new value. False (nothing changed) when <paramref name="value"/> is refused by <see cref="Check"/> or its
    /// canonical form equals the current value.
    /// </summary>
    /// <param name="value">The value as the administrator sent it.</param>
    /// <param name="at">When.</param>
    /// <param name="byUserId">The acting user's internal id, or null when unknown.</param>
    public bool Change(string value, DateTimeOffset at, long? byUserId)
    {
        var check = Check(value);
        if (!check.IsValid || string.Equals(check.CanonicalValue, Value, StringComparison.Ordinal))
        {
            return false;
        }

        Value = check.CanonicalValue!;
        UpdatedAt = at;
        UpdatedByUserId = byUserId;
        return true;
    }
}

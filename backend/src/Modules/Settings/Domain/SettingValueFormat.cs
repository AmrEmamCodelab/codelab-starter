using System.Globalization;
using Starter.Application.Settings;

namespace Starter.Modules.Settings.Domain;

/// <summary>
/// The one owner of how a setting value is written and read for each <see cref="SettingValueType"/>: integers and
/// durations in invariant culture, booleans as lower-case <c>true</c>/<c>false</c>, durations as whole non-negative
/// seconds. The admin update and the runtime reader both go through here.
/// </summary>
public static class SettingValueFormat
{
    /// <summary>The stored text of <see langword="true"/>.</summary>
    public const string True = "true";

    /// <summary>The stored text of <see langword="false"/>.</summary>
    public const string False = "false";

    /// <summary>
    /// Checks <paramref name="value"/> against <paramref name="valueType"/> and <paramref name="range"/> and returns the
    /// canonical stored form. Text is kept as given (it may be empty); every other type is trimmed first.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="valueType"/> is not a defined member.</exception>
    public static SettingValueCheck Check(SettingValueType valueType, string value, SettingRange range)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(range);
        if (value.Length > SettingFieldLimits.ValueMaxLength)
        {
            return SettingValueCheck.Refused(SettingValueProblem.TooLong);
        }

        return valueType switch
        {
            SettingValueType.Text => SettingValueCheck.Accepted(value),
            SettingValueType.WholeNumber => CheckNumber(TryReadInt32(value, out var number), number, range),
            SettingValueType.Boolean => CheckBoolean(value),
            SettingValueType.DurationSeconds => CheckNumber(TryReadSeconds(value, out var seconds), seconds, range),
            _ => throw new ArgumentOutOfRangeException(nameof(valueType), valueType, "Unknown setting value type."),
        };
    }

    /// <summary>Reads a 32-bit integer (optional sign, invariant culture, surrounding blanks ignored).</summary>
    public static bool TryReadInt32(string value, out int result) =>
        int.TryParse(value?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);

    /// <summary>Reads <c>true</c> or <c>false</c> (any case, surrounding blanks ignored).</summary>
    public static bool TryReadBoolean(string value, out bool result)
    {
        var trimmed = value?.Trim();
        result = string.Equals(trimmed, True, StringComparison.OrdinalIgnoreCase);
        return result || string.Equals(trimmed, False, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads a duration stored as whole non-negative seconds.</summary>
    public static bool TryReadDuration(string value, out TimeSpan result)
    {
        var parsed = TryReadSeconds(value, out var seconds);
        result = parsed ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
        return parsed;
    }

    /// <summary>The stored text of a boolean.</summary>
    public static string Write(bool value) => value ? True : False;

    /// <summary>The stored text of an integer or a number of seconds.</summary>
    public static string Write(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool TryReadSeconds(string value, out int seconds) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out seconds);

    private static SettingValueCheck CheckBoolean(string value) =>
        TryReadBoolean(value, out var flag)
            ? SettingValueCheck.Accepted(Write(flag))
            : SettingValueCheck.Refused(SettingValueProblem.InvalidFormat);

    private static SettingValueCheck CheckNumber(bool parsed, int number, SettingRange range)
    {
        if (!parsed)
        {
            return SettingValueCheck.Refused(SettingValueProblem.InvalidFormat);
        }

        return range.Contains(number)
            ? SettingValueCheck.Accepted(Write(number))
            : SettingValueCheck.Refused(SettingValueProblem.OutOfRange);
    }
}

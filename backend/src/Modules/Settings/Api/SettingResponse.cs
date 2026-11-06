using Starter.Application.Settings;
using Starter.Modules.Settings.Domain;

namespace Starter.Modules.Settings.Api;

/// <summary>One runtime setting on the wire (the bounds stay server-side; a value outside them is refused with a field error).</summary>
/// <param name="Key">The stable key (<c>&lt;group&gt;.&lt;kebab-name&gt;</c>).</param>
/// <param name="Group">The group the screen lists it under.</param>
/// <param name="ValueType">How the value reads (integer on the wire).</param>
/// <param name="Value">The current value, canonical text.</param>
/// <param name="DefaultValue">The seeded value, canonical text.</param>
/// <param name="DescriptionAr">What it controls, in Arabic.</param>
/// <param name="DescriptionEn">What it controls, in English.</param>
/// <param name="UpdatedAt">When an administrator last changed it (ISO 8601 UTC), or null.</param>
public sealed record SettingResponse(
    string Key,
    string Group,
    SettingValueType ValueType,
    string Value,
    string DefaultValue,
    string DescriptionAr,
    string DescriptionEn,
    DateTimeOffset? UpdatedAt)
{
    /// <summary>The wire shape of <paramref name="setting"/>.</summary>
    public static SettingResponse From(Setting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return new SettingResponse(
            setting.Key,
            setting.Group,
            setting.ValueType,
            setting.Value,
            setting.DefaultValue,
            setting.DescriptionAr,
            setting.DescriptionEn,
            setting.UpdatedAt?.ToUniversalTime());
    }

    /// <summary>The wire shape of a list of settings, in order.</summary>
    public static IReadOnlyList<SettingResponse> ListFrom(IReadOnlyList<Setting> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return [.. settings.Select(From)];
    }
}

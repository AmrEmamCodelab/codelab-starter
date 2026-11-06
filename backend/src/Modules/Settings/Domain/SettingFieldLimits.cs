namespace Starter.Modules.Settings.Domain;

/// <summary>The Settings column sizes; the validators and the table use the same constants.</summary>
public static class SettingFieldLimits
{
    /// <summary>The longest setting key (<c>&lt;group&gt;.&lt;kebab-name&gt;</c>).</summary>
    public const int KeyMaxLength = 100;

    /// <summary>The longest group name.</summary>
    public const int GroupMaxLength = 50;

    /// <summary>The longest value (and default value), in characters.</summary>
    public const int ValueMaxLength = 2000;

    /// <summary>The longest description, in either language.</summary>
    public const int DescriptionMaxLength = 500;
}

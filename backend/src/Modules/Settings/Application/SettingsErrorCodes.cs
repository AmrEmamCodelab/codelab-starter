namespace Starter.Modules.Settings.Application;

/// <summary>
/// The Settings module's error codes and field message codes. The client renders <c>errors.&lt;CODE&gt;</c> from its ar
/// and en bundles, for a refusal code and for a field message alike.
/// </summary>
public static class SettingsErrorCodes
{
    /// <summary>No setting has this key (404). Settings are added by migrations only.</summary>
    public const string SettingNotFound = "SETTINGS_SETTING_NOT_FOUND";

    /// <summary>
    /// Field message on <c>value</c>: the number is outside the setting's minimum and maximum (with
    /// <c>VALIDATION_FAILED</c>, 400). A value of the wrong type carries <c>INVALID_FORMAT</c> instead.
    /// </summary>
    public const string ValueOutOfRange = "SETTINGS_VALUE_OUT_OF_RANGE";

    // scaffold:error-codes
}

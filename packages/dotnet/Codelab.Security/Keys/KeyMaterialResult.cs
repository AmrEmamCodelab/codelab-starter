namespace Codelab.Security.Keys;

/// <summary>The verdict of a key-material check. <see cref="Error"/> names the setting, never the value.</summary>
public readonly record struct KeyMaterialResult(bool IsValid, string? Error)
{
    /// <summary>A passing verdict.</summary>
    public static KeyMaterialResult Ok() => new(IsValid: true, Error: null);

    /// <summary>A failing verdict with its message.</summary>
    public static KeyMaterialResult Fail(string error) => new(IsValid: false, Error: error);
}

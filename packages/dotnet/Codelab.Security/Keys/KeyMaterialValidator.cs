namespace Codelab.Security.Keys;

/// <summary>
/// Structural checks for key material and key rings: present, not a placeholder, valid base64 decoding to at least
/// <see cref="MinimumKeyBytes"/> bytes, unique ids, an active id that is a member. Messages name the setting and
/// never echo the value. The published-material check is separate (<see cref="KeyRingGuard"/>), because every
/// structural check passes on a committed key: it is well-formed and exactly the right length.
/// </summary>
public static class KeyMaterialValidator
{
    /// <summary>The minimum decoded key length (256 bits).</summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>The marker committed templates carry instead of a key.</summary>
    public const string PlaceholderMarker = "CHANGE-IN-PROD";

    private const int BitsPerByte = 8;

    /// <summary>Checks one base64 key setting.</summary>
    public static KeyMaterialResult ValidateBase64Key(string? value, string settingPath)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return KeyMaterialResult.Fail($"{settingPath} is required and must not be empty.");
        }

        if (value.Contains(PlaceholderMarker, StringComparison.Ordinal))
        {
            return KeyMaterialResult.Fail($"{settingPath} is still the committed placeholder. Generate real key material (init.ps1) and supply it from the environment or a secret store.");
        }

        var buffer = new byte[value.Length];
        if (!Convert.TryFromBase64String(value, buffer, out var decodedLength))
        {
            return KeyMaterialResult.Fail($"{settingPath} is not valid base64.");
        }

        return decodedLength < MinimumKeyBytes
            ? KeyMaterialResult.Fail($"{settingPath} decodes to {decodedLength} bytes; at least {MinimumKeyBytes} ({MinimumKeyBytes * BitsPerByte} bits) are required.")
            : KeyMaterialResult.Ok();
    }

    /// <summary>Checks a ring: at least one key, unique non-empty ids, valid material, an active id that is a member.</summary>
    public static KeyMaterialResult ValidateRing(KeyRingOptions ring, string sectionPath)
    {
        ArgumentNullException.ThrowIfNull(ring);
        if (ring.Keys.Count == 0)
        {
            return KeyMaterialResult.Fail($"{sectionPath}:Keys must contain at least one key.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in ring.Keys)
        {
            var result = ValidateEntry(entry, seen, sectionPath);
            if (!result.IsValid)
            {
                return result;
            }
        }

        if (string.IsNullOrWhiteSpace(ring.ActiveKeyId))
        {
            return KeyMaterialResult.Fail($"{sectionPath}:ActiveKeyId is required.");
        }

        return seen.Contains(ring.ActiveKeyId)
            ? KeyMaterialResult.Ok()
            : KeyMaterialResult.Fail($"{sectionPath}:ActiveKeyId '{ring.ActiveKeyId}' is not one of the configured Keys; the first write would fail.");
    }

    private static KeyMaterialResult ValidateEntry(KeyEntryOptions entry, HashSet<string> seen, string sectionPath)
    {
        if (string.IsNullOrWhiteSpace(entry.KeyId))
        {
            return KeyMaterialResult.Fail($"{sectionPath}:Keys contains an entry with no KeyId.");
        }

        if (!seen.Add(entry.KeyId))
        {
            return KeyMaterialResult.Fail($"{sectionPath}:Keys contains the KeyId '{entry.KeyId}' twice; a key id must select exactly one key.");
        }

        return ValidateBase64Key(entry.MaterialBase64, $"{sectionPath}:Keys[{entry.KeyId}]:MaterialBase64");
    }
}

namespace Starter.Domain.Lookups;

/// <summary>The column sizes and key shape every lookup table shares (one owner for the schema and the checks).</summary>
public static class LookupLimits
{
    /// <summary>The longest lookup type key (<see cref="LookupTypes"/>).</summary>
    public const int TypeKeyMaxLength = 64;

    /// <summary>The longest value code (<see cref="LookupEntity.Code"/>).</summary>
    public const int CodeMaxLength = 64;

    /// <summary>The longest Arabic or English name.</summary>
    public const int NameMaxLength = 200;

    private const char Hyphen = '-';

    /// <summary>
    /// True when <paramref name="key"/> is a well-formed lookup type key or value code: kebab-case ASCII (lower-case
    /// letters and digits in words joined by single hyphens, starting with a letter), at most
    /// <paramref name="maxLength"/> characters.
    /// </summary>
    public static bool IsKebab(string? key, int maxLength)
    {
        if (string.IsNullOrEmpty(key) || key.Length > maxLength || !char.IsAsciiLetterLower(key[0]))
        {
            return false;
        }

        return key.Split(Hyphen).All(IsWord);
    }

    private static bool IsWord(string word) =>
        word.Length > 0 && word.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character));
}

namespace Starter.Modules.Users.Domain;

/// <summary>
/// The one definition of an organization code: trimmed, upper-case (invariant), ASCII letters, digits, <c>_</c> and
/// <c>-</c>, at most <see cref="UsersLimits.OrganizationCodeMaxLength"/> characters. Codes are compared in this
/// normalized form everywhere (the admin screens, the import sheet and the unique index).
/// </summary>
public static class OrganizationCodes
{
    private const char Underscore = '_';
    private const char Hyphen = '-';

    /// <summary>The stored form of <paramref name="code"/>: trimmed and upper-case.</summary>
    public static string Normalize(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return code.Trim().ToUpperInvariant();
    }

    /// <summary>True when the normalized <paramref name="code"/> is non-empty, short enough and uses allowed characters only.</summary>
    public static bool IsWellFormed(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var normalized = Normalize(code);
        return normalized.Length <= UsersLimits.OrganizationCodeMaxLength && normalized.All(IsCodeCharacter);
    }

    private static bool IsCodeCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is Underscore or Hyphen;
}

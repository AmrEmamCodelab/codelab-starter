namespace Starter.Application.Jobs;

/// <summary>
/// The shape of a job key: lowercase ASCII letters, digits and single hyphens, starting with a letter, ending with a
/// letter or digit, <see cref="MinLength"/> to <see cref="MaxLength"/> characters (<c>outbox-drain</c>). The key is a
/// recurring schedule id and a URL segment, so it never needs escaping.
/// </summary>
public static class JobKeyFormat
{
    /// <summary>The shortest key.</summary>
    public const int MinLength = 3;

    /// <summary>The longest key (the run table column).</summary>
    public const int MaxLength = 64;

    /// <summary>The separator between words.</summary>
    public const char Separator = '-';

    /// <summary>True when <paramref name="key"/> is a well-formed job key.</summary>
    public static bool IsValid(string? key)
    {
        if (key is null || key.Length is < MinLength or > MaxLength)
        {
            return false;
        }

        if (!char.IsAsciiLetterLower(key[0]) || key[^1] == Separator)
        {
            return false;
        }

        return key.All(IsAllowed) && !key.Contains($"{Separator}{Separator}", StringComparison.Ordinal);
    }

    private static bool IsAllowed(char character) =>
        char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == Separator;
}

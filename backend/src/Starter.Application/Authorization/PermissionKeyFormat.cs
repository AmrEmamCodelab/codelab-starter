using System.Text.RegularExpressions;

namespace Starter.Application.Authorization;

/// <summary>The one format of a permission key: dotted lower-case segments, for example <c>dashboard.view</c>.</summary>
public static partial class PermissionKeyFormat
{
    /// <summary>
    /// The pattern every key matches: at least two segments separated by dots, each starting with a lower-case letter
    /// followed by lower-case letters, digits or dashes. The module prefix is not required.
    /// </summary>
    public const string Pattern = @"^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$";

    /// <summary>The longest key the catalog accepts (the database column holds this many characters).</summary>
    public const int MaxLength = 128;

    /// <summary>The separator between segments.</summary>
    public const char Separator = '.';

    /// <summary>True when <paramref name="key"/> matches <see cref="Pattern"/> and is at most <see cref="MaxLength"/> characters.</summary>
    public static bool IsValid(string? key) =>
        key is { Length: > 0 and <= MaxLength } && KeyPattern().IsMatch(key);

    /// <summary>The owner segment of a key (the part before the first dot; the whole key when it has none).</summary>
    public static string OwnerOf(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var separator = key.IndexOf(Separator, StringComparison.Ordinal);
        return separator < 0 ? key : key[..separator];
    }

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}

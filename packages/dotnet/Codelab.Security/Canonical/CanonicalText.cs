using System.Text;

namespace Codelab.Security.Canonical;

/// <summary>
/// Culture-free normalisations shared by both ends of the wire: the HTTP method allowlist and percent-encoded path
/// normalisation. No method here performs culture-sensitive case mapping (the primary culture is ar-SA, and a Turkish
/// host would otherwise fold <c>i</c> differently).
/// </summary>
public static class CanonicalText
{
    private const char Escape = '%';
    private const int EscapeLength = 3;
    private const int HexShift = 4;
    private const int HexLetterOffset = 10;
    private const int NotHex = -1;

    private static readonly string[] AllowedMethods = ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"];

    /// <summary>The methods that may appear in a canonical form (a closed allowlist).</summary>
    public static IReadOnlyList<string> Methods => AllowedMethods;

    /// <summary>
    /// Maps <paramref name="method"/> onto its allowlist literal (ordinal, case-insensitive match). On failure
    /// <paramref name="canonical"/> is empty, so a caller that ignores the result signs an empty method, never the
    /// caller's spelling. Request paths use this one: an odd verb is a refusal, not an exception.
    /// </summary>
    public static bool TryCanonicalMethod(string? method, out string canonical)
    {
        foreach (var allowed in AllowedMethods)
        {
            if (string.Equals(method, allowed, StringComparison.OrdinalIgnoreCase))
            {
                canonical = allowed;
                return true;
            }
        }

        canonical = string.Empty;
        return false;
    }

    /// <summary>The allowlist literal for <paramref name="method"/>, or throws.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The method is not in the allowlist.</exception>
    public static string CanonicalMethod(string method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return TryCanonicalMethod(method, out var canonical)
            ? canonical
            : throw new ArgumentOutOfRangeException(nameof(method), "The method is not in the canonical allowlist.");
    }

    /// <summary>
    /// Normalises a percent-encoded path: escapes of RFC 3986 unreserved characters are decoded and every remaining
    /// escape is upper-cased (<c>%2f</c> and <c>%2F</c> sign alike). Reserved and non-ASCII escapes stay encoded:
    /// decoding <c>%2F</c> would turn one segment into two. Both ends run this, so they agree by construction.
    /// </summary>
    public static string NormalizePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var builder = new StringBuilder(path.Length);
        var index = 0;
        while (index < path.Length)
        {
            index += AppendNormalized(path, index, builder);
        }

        return builder.ToString();
    }

    private static int AppendNormalized(string path, int index, StringBuilder builder)
    {
        if (path[index] != Escape || index + EscapeLength > path.Length)
        {
            builder.Append(path[index]);
            return 1;
        }

        var high = HexValue(path[index + 1]);
        var low = HexValue(path[index + EscapeLength - 1]);
        if (high == NotHex || low == NotHex)
        {
            builder.Append(path[index]);
            return 1;
        }

        var value = (high << HexShift) | low;
        if (IsUnreserved(value))
        {
            builder.Append((char)value);
        }
        else
        {
            builder.Append(Escape).Append(char.ToUpperInvariant(path[index + 1])).Append(char.ToUpperInvariant(path[index + EscapeLength - 1]));
        }

        return EscapeLength;
    }

    private static bool IsUnreserved(int c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.' or '_' or '~';

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + HexLetterOffset,
        >= 'A' and <= 'F' => c - 'A' + HexLetterOffset,
        _ => NotHex,
    };
}

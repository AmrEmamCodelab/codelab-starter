using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Codelab.Testing.Sweeps;

/// <summary>
/// Glob matching for repo-relative, forward-slash paths, with the same semantics as the repository's scanners:
/// <c>**</c> any number of segments, <c>*</c> within a segment, <c>?</c> one character, <c>{a,b}</c> alternation. A
/// pattern without <c>/</c> matches the file name at any depth.
/// </summary>
public static class GlobPattern
{
    private const char Separator = '/';
    private const char Star = '*';
    private const string AnyDepthPrefix = "**/";
    private const string CurrentDirectoryPrefix = "./";
    private const int DoubleStarLength = 2;
    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    /// <summary>True when <paramref name="path"/> (any slash style) matches <paramref name="pattern"/>.</summary>
    public static bool IsMatch(string path, string pattern)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        return Cache.GetOrAdd(pattern, Compile).IsMatch(ToPosix(path));
    }

    /// <summary>Forward slashes, no leading <c>./</c>.</summary>
    public static string ToPosix(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var posix = path.Replace('\\', Separator);
        return posix.StartsWith(CurrentDirectoryPrefix, StringComparison.Ordinal) ? posix[CurrentDirectoryPrefix.Length..] : posix;
    }

    private static Regex Compile(string pattern)
    {
        var effective = pattern.Contains(Separator, StringComparison.Ordinal) ? pattern : AnyDepthPrefix + pattern;
        return new Regex($"^{Translate(effective)}$", RegexOptions.CultureInvariant);
    }

    private static string Translate(string glob)
    {
        var source = new StringBuilder();
        var braceDepth = 0;
        for (var index = 0; index < glob.Length; index++)
        {
            var current = glob[index];
            if (current == Star && index + 1 < glob.Length && glob[index + 1] == Star)
            {
                var afterStars = index + DoubleStarLength;
                var followedBySlash = afterStars < glob.Length && glob[afterStars] == Separator;
                source.Append(followedBySlash ? "(?:.*/)?" : ".*");
                index += followedBySlash ? DoubleStarLength : 1;
                continue;
            }

            source.Append(TranslateChar(current, ref braceDepth));
        }

        return source.ToString();
    }

    private static string TranslateChar(char current, ref int braceDepth)
    {
        switch (current)
        {
            case Star:
                return "[^/]*";
            case '?':
                return "[^/]";
            case '{':
                braceDepth++;
                return "(?:";
            case '}' when braceDepth > 0:
                braceDepth--;
                return ")";
            case ',' when braceDepth > 0:
                return "|";
            default:
                return Regex.Escape(current.ToString());
        }
    }
}

namespace Starter.Tools.DocsGate.Repository;

/// <summary>
/// Existence check that is case-sensitive below the root on every OS. Windows and macOS file systems ignore case,
/// so a link with the wrong case works locally and breaks on Linux CI and in browsers.
/// </summary>
internal static class ExactCasePath
{
    private const string ParentSegment = "..";
    private const string CurrentSegment = ".";
    private static readonly char[] SearchWildcards = ['*', '?'];
    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public static bool Exists(string root, string fullPath)
    {
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            return false;
        }

        var relative = Path.GetRelativePath(root, fullPath);
        if (IsOutsideRoot(relative))
        {
            // Only existence can be checked outside the root; the root's own spelling is the caller's.
            return true;
        }

        return SegmentsMatchExactly(root, relative.Split(Separators, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsOutsideRoot(string relative) =>
        Path.IsPathRooted(relative)
        || string.Equals(relative, ParentSegment, StringComparison.Ordinal)
        || relative.StartsWith(ParentSegment + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static bool SegmentsMatchExactly(string root, IEnumerable<string> segments)
    {
        var current = root;
        foreach (var segment in segments)
        {
            if (string.Equals(segment, CurrentSegment, StringComparison.Ordinal))
            {
                continue;
            }

            // A wildcard cannot be used as a search pattern; that segment is accepted as already proven to exist.
            var spelledExactly = segment.IndexOfAny(SearchWildcards) >= 0
                || Directory.EnumerateFileSystemEntries(current, segment)
                    .Any(entry => string.Equals(Path.GetFileName(entry), segment, StringComparison.Ordinal));
            if (!spelledExactly)
            {
                return false;
            }

            current = Path.Combine(current, segment);
        }

        return true;
    }
}

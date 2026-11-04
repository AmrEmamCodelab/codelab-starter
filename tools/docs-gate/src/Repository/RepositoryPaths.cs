namespace Starter.Tools.DocsGate.Repository;

/// <summary>Root-relative paths always use '/' (as git prints them); this class converts to and from OS paths.</summary>
internal static class RepositoryPaths
{
    public const char Separator = '/';

    public static string ToFullPath(string root, string relativePath) =>
        Path.GetFullPath(Path.Combine(root, relativePath.Replace(Separator, Path.DirectorySeparatorChar)));

    public static string ToRelative(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, Separator);

    public static string Combine(string directory, string fileName) =>
        directory.Length == 0 ? fileName : directory + Separator + fileName;

    public static string FileName(string relativePath)
    {
        var index = relativePath.LastIndexOf(Separator);
        return index < 0 ? relativePath : relativePath[(index + 1)..];
    }

    /// <summary>The containing folder, '' for a file at the root.</summary>
    public static string DirectoryOf(string relativePath)
    {
        var index = relativePath.LastIndexOf(Separator);
        return index < 0 ? string.Empty : relativePath[..index];
    }

    public static IEnumerable<string> DirectorySegments(string relativePath) =>
        DirectoryOf(relativePath).Split(Separator, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>True when <paramref name="path"/> lies inside <paramref name="directory"/> ('' contains everything).</summary>
    public static bool IsUnder(string path, string directory) =>
        directory.Length == 0 || path.StartsWith(directory + Separator, StringComparison.Ordinal);
}

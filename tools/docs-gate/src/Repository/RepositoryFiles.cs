namespace Starter.Tools.DocsGate.Repository;

/// <summary>Lists the files the gate checks, root-relative with '/' separators, sorted.</summary>
internal static class RepositoryFiles
{
    /// <summary>
    /// In a git work tree: tracked plus untracked-but-not-ignored files that exist on disk (so .gitignore is honoured).
    /// Elsewhere: every file under <paramref name="root"/>. Both skip <see cref="RepositoryLayout.ExcludedDirectoryNames"/>.
    /// </summary>
    public static IReadOnlyList<string> List(string root, IGitClient git, bool useGit)
    {
        var candidates = useGit ? ListFromGit(root, git) : ListFromDisk(root);
        return [.. candidates
            .Where(path => !RepositoryLayout.IsInExcludedDirectory(path))
            .Where(path => File.Exists(RepositoryPaths.ToFullPath(root: root, relativePath: path)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    private static IEnumerable<string> ListFromGit(string root, IGitClient git) =>
        git.RunChecked(root, "ls-files", "-z", "--cached", "--others", "--exclude-standard").NulSeparatedPaths();

    private static IEnumerable<string> ListFromDisk(string root)
    {
        var pending = new Stack<string>([root]);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                yield return RepositoryPaths.ToRelative(root: root, fullPath: file);
            }

            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (!RepositoryLayout.ExcludedDirectoryNames.Contains(Path.GetFileName(child)))
                {
                    pending.Push(child);
                }
            }
        }
    }
}

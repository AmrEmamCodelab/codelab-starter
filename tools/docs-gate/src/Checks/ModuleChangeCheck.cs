using Starter.Tools.DocsGate.Repository;

namespace Starter.Tools.DocsGate.Checks;

/// <summary>DOCS005: a module whose files changed must also change its MODULE.md, unless Docs-Not-Needed is given.</summary>
internal static class ModuleChangeCheck
{
    private const int ListedFilesLimit = 3;

    /// <param name="moduleDirectories">Root-relative module folders ('' for a module at the root).</param>
    /// <param name="changedFiles">Root-relative changed paths with '/' separators.</param>
    /// <param name="docsNotNeededReason">Override reason, or null when no override was given.</param>
    /// <param name="notices">Receives one line per module that the override let through.</param>
    public static IReadOnlyList<Finding> Check(
        IReadOnlyCollection<string> moduleDirectories,
        IReadOnlyCollection<string> changedFiles,
        string? docsNotNeededReason,
        ICollection<string> notices)
    {
        var findings = new List<Finding>();
        foreach (var (moduleDirectory, files) in GroupByModule(moduleDirectories, changedFiles))
        {
            var docPath = RepositoryLayout.ModuleDocPath(moduleDirectory);
            if (files.Contains(docPath, StringComparer.Ordinal))
            {
                continue;
            }

            if (docsNotNeededReason is not null)
            {
                notices.Add($"{docPath} not updated; accepted by '{DocsNotNeededOverride.TrailerKey} {docsNotNeededReason}'.");
                continue;
            }

            findings.Add(new Finding(
                RuleIds.ModuleChangedWithoutDocs,
                docPath,
                Finding.NoLine,
                $"module changed ({Describe(files)}) without a {RepositoryLayout.ModuleDocFileName} change. " +
                $"Update it, or add '{DocsNotNeededOverride.TrailerKey} <reason>' to the commit message or PR description."));
        }

        return findings;
    }

    // Each changed file belongs to the deepest module folder that contains it.
    private static SortedDictionary<string, List<string>> GroupByModule(
        IReadOnlyCollection<string> moduleDirectories,
        IReadOnlyCollection<string> changedFiles)
    {
        var deepestFirst = moduleDirectories.OrderByDescending(directory => directory.Length).ToList();
        var groups = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in changedFiles)
        {
            var owner = deepestFirst.FirstOrDefault(directory => RepositoryPaths.IsUnder(path: file, directory: directory));
            if (owner is null)
            {
                continue;
            }

            if (!groups.TryGetValue(owner, out var files))
            {
                files = [];
                groups[owner] = files;
            }

            files.Add(file);
        }

        return groups;
    }

    private static string Describe(List<string> files)
    {
        var listed = string.Join(", ", files.Take(ListedFilesLimit));
        var more = files.Count - ListedFilesLimit;
        return more > 0 ? FormattableString.Invariant($"{listed} and {more} more") : listed;
    }
}

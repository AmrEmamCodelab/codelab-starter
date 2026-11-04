namespace Starter.Tools.DocsGate.Repository;

/// <summary>Owns the repo conventions the gate checks: module markers, doc names, required sections, skipped folders.</summary>
internal static class RepositoryLayout
{
    public const string ModuleRulesFileName = "module.rules.yaml";
    public const string ModuleDocFileName = "MODULE.md";
    public const string MarkdownExtension = ".md";

    /// <summary>Headings every MODULE.md must have (any heading level, case-insensitive).</summary>
    public static readonly IReadOnlyList<string> RequiredModuleDocSections = ["Purpose", "Use", "Extend", "Rules", "Gotchas"];

    /// <summary>Folders never scanned, even outside git (generated output, dependencies, VCS data).</summary>
    public static readonly IReadOnlySet<string> ExcludedDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", ".vs", ".angular", "TestResults",
    };

    /// <summary>Folders that hold a module.rules.yaml, root-relative ('' for the root itself).</summary>
    public static IReadOnlyList<string> FindModuleDirectories(IEnumerable<string> files) =>
        [.. files
            .Where(file => string.Equals(RepositoryPaths.FileName(file), ModuleRulesFileName, StringComparison.Ordinal))
            .Select(RepositoryPaths.DirectoryOf)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    public static string ModuleDocPath(string moduleDirectory) =>
        RepositoryPaths.Combine(directory: moduleDirectory, fileName: ModuleDocFileName);

    public static bool IsMarkdown(string path) =>
        path.EndsWith(MarkdownExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Files the merge-marker rule scans: Markdown and module rules.</summary>
    public static bool IsDocumentation(string path) =>
        IsMarkdown(path) || string.Equals(RepositoryPaths.FileName(path), ModuleRulesFileName, StringComparison.Ordinal);

    public static bool IsInExcludedDirectory(string relativePath) =>
        RepositoryPaths.DirectorySegments(relativePath).Any(ExcludedDirectoryNames.Contains);
}

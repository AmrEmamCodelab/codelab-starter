using Codelab.Testing.Architecture;
using Codelab.Testing.Checks;

namespace Codelab.Testing.Sweeps;

/// <summary>
/// The security sweep of one deployable (an API, a service, a web app): the repository's source-product term list and
/// exception list under <see cref="ExtractFolder"/>, plus <see cref="SweepRules.Secrets"/>, over the deployable's
/// folder only. Each deployable's test project calls <see cref="Check"/> once, so a leak fails that deployable's tests.
/// </summary>
public static class DeployableSweep
{
    /// <summary>The repository folder that holds the term list and the exception list.</summary>
    public const string ExtractFolder = "tools/extract";

    /// <summary>The file pattern of the term list; exactly one file in <see cref="ExtractFolder"/> matches it.</summary>
    public const string TermListPattern = "*-terms.txt";

    /// <summary>The exception list file in <see cref="ExtractFolder"/>.</summary>
    public const string ExceptionListFileName = "scrub-exceptions.txt";

    /// <summary>
    /// Machine-local files the repository's .gitignore keeps out of source control (generated local secrets, key and
    /// certificate files, environment files). They are never committed, so the sweep never reads them; the repository's
    /// own scanners skip them through git for the same reason.
    /// </summary>
    public static IReadOnlyList<string> LocalOnlyFiles { get; } =
        ["appsettings.*.local.json", "launchSettings.local.json", ".env", ".env.*", "*.pfx", "*.p12", "*.key", "*.pem"];

    /// <summary>Machine-local output folders the repository's .gitignore keeps out of source control.</summary>
    public static IReadOnlyList<string> LocalOnlyDirectoryNames { get; } = ["coverage", ".vs", ".idea"];

    /// <summary>The term rules of the repository plus the secret rules.</summary>
    /// <exception cref="InvalidOperationException">The extract folder does not hold exactly one term list.</exception>
    public static IReadOnlyList<SweepRule> Rules(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var termFiles = Directory.EnumerateFiles(ExtractPath(repositoryRoot), TermListPattern).ToList();
        if (termFiles.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one term list ({TermListPattern}) in {ExtractFolder}, found {termFiles.Count}.");
        }

        return [.. SweepConfigurationFiles.ReadTermRules(termFiles[0]), .. SweepRules.Secrets()];
    }

    /// <summary>The repository's exception list (repository-relative globs).</summary>
    public static IReadOnlyList<SweepAllowance> Allowances(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        return SweepConfigurationFiles.ReadExceptions(Path.Combine(ExtractPath(repositoryRoot), ExceptionListFileName));
    }

    /// <summary>
    /// Sweeps <paramref name="deployableFolders"/> (repository-relative, for example <c>backend</c>) with the
    /// repository's rules and exceptions. The repository root is found from <paramref name="startDirectory"/>
    /// (default: the test output folder) by <see cref="CompiledRules.RelativePath"/>.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">A deployable folder does not exist.</exception>
    public static CheckResult Check(IReadOnlyList<string> deployableFolders, string? startDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(deployableFolders);
        if (deployableFolders.Count == 0)
        {
            throw new ArgumentException("At least one deployable folder is required.", nameof(deployableFolders));
        }

        var root = RepositoryRoot.Find(CompiledRules.RelativePath, startDirectory);
        var options = new SourceSweepOptions
        {
            Root = root,
            IncludedDirectories = [.. deployableFolders],
            ExcludedDirectoryNames = [.. new SourceSweepOptions { Root = root }.ExcludedDirectoryNames, .. LocalOnlyDirectoryNames],
            ExcludedPaths = [.. LocalOnlyFiles],
            Allowances = [.. Allowances(root)],
        };
        return SourceSweep.Check(options, Rules(root));
    }

    private static string ExtractPath(string repositoryRoot) => Path.Combine(repositoryRoot, ExtractFolder);
}

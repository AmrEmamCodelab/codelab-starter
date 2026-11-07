namespace Codelab.Testing.Sweeps;

/// <summary>What a source sweep reads.</summary>
public sealed class SourceSweepOptions
{
    /// <summary>The folder to sweep (usually the repository root); findings use paths relative to it.</summary>
    public required string Root { get; init; }

    /// <summary>
    /// Folders under <see cref="Root"/> to sweep, relative to it (for example one deployable's folder). Empty means the
    /// whole root. Findings and allowance globs stay relative to <see cref="Root"/>, so a repository's exception list
    /// applies unchanged.
    /// </summary>
    public IList<string> IncludedDirectories { get; init; } = [];

    /// <summary>File extensions to read, with the dot (<c>.cs</c>). Empty means every file that is not skipped.</summary>
    public IList<string> IncludeExtensions { get; init; } = [];

    /// <summary>Folder names never descended into.</summary>
    public IList<string> ExcludedDirectoryNames { get; init; } = ["bin", "obj", "node_modules", ".git", "dist", ".angular", "TestResults"];

    /// <summary>Repo-relative globs of files never read (for example the sweep's own configuration).</summary>
    public IList<string> ExcludedPaths { get; init; } = [];

    /// <summary>Where a rule may match on purpose (the exception list).</summary>
    public IList<SweepAllowance> Allowances { get; init; } = [];

    /// <summary>Files larger than this are skipped as not source (bytes).</summary>
    public long MaxFileBytes { get; init; } = SourceSweepDefaults.MaxFileBytes;
}

/// <summary>Default limits of a source sweep.</summary>
public static class SourceSweepDefaults
{
    /// <summary>Two mebibytes: generated bundles and binaries above it are not source.</summary>
    public const long MaxFileBytes = 2L * 1024 * 1024;
}

namespace Starter.Tools.DocsGate.Repository;

/// <summary>The git commands the docs gate needs. A port so tests can run against real or fake repositories.</summary>
internal interface IGitClient
{
    /// <summary>True when <paramref name="directory"/> is inside a git work tree (false when git is not installed).</summary>
    bool IsWorkTree(string directory);

    /// <summary>Runs git with <paramref name="arguments"/> in <paramref name="directory"/>; never throws on a non-zero exit.</summary>
    GitResult Run(string directory, IReadOnlyList<string> arguments);
}

/// <summary>Exit code and captured output of one git command.</summary>
internal sealed record GitResult(int ExitCode, string Output, string Error)
{
    public const int SuccessExitCode = 0;

    public bool Succeeded => ExitCode == SuccessExitCode;
}

/// <summary>A git command the docs gate depends on failed; the gate cannot give a reliable answer.</summary>
internal sealed class GitCommandException(string message) : Exception(message);

internal static class GitClientExtensions
{
    /// <summary>Runs git and throws <see cref="GitCommandException"/> on a non-zero exit.</summary>
    public static GitResult RunChecked(this IGitClient git, string directory, params string[] arguments)
    {
        var result = git.Run(directory, arguments);
        if (!result.Succeeded)
        {
            throw new GitCommandException(
                FormattableString.Invariant($"'git {string.Join(' ', arguments)}' failed with exit code {result.ExitCode}: {result.Error.Trim()}"));
        }

        return result;
    }

    /// <summary>Splits NUL-separated output (git's -z flag) into paths.</summary>
    public static IEnumerable<string> NulSeparatedPaths(this GitResult result) =>
        result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
}

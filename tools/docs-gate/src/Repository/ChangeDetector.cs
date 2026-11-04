namespace Starter.Tools.DocsGate.Repository;

/// <summary>Lists changed files and the latest commit message of the work tree at <c>root</c>.</summary>
internal sealed class ChangeDetector(IGitClient git, string root)
{
    private const string Head = "HEAD";

    /// <summary>
    /// Root-relative paths ('/' separators) that differ from the comparison point, plus untracked files.
    /// With <paramref name="baseRef"/>: from the merge base of that ref and HEAD to the working tree.
    /// Without: from HEAD to the working tree (staged and unstaged); on a branch with no commits, every file counts.
    /// Renames are reported as a delete plus an add so both modules see the change.
    /// </summary>
    public IReadOnlyCollection<string> ChangedFiles(string? baseRef)
    {
        var reference = baseRef is null ? HeadOrNull() : MergeBase(baseRef);
        var changed = new SortedSet<string>(StringComparer.Ordinal);
        if (reference is null)
        {
            changed.UnionWith(git.RunChecked(root, "diff", "--cached", "--name-only", "--relative", "--no-renames", "-z").NulSeparatedPaths());
            changed.UnionWith(git.RunChecked(root, "diff", "--name-only", "--relative", "--no-renames", "-z").NulSeparatedPaths());
        }
        else
        {
            changed.UnionWith(git.RunChecked(root, "diff", "--name-only", "--relative", "--no-renames", "-z", reference).NulSeparatedPaths());
        }

        changed.UnionWith(git.RunChecked(root, "ls-files", "--others", "--exclude-standard", "-z").NulSeparatedPaths());
        return changed;
    }

    /// <summary>The full message of HEAD, or null when the branch has no commits.</summary>
    public string? LatestCommitMessage() =>
        HeadOrNull() is null ? null : git.RunChecked(root, "log", "-1", "--format=%B").Output;

    private string? HeadOrNull() =>
        git.Run(root, ["rev-parse", "--verify", "--quiet", Head]).Succeeded ? Head : null;

    private string MergeBase(string baseRef)
    {
        var result = git.Run(root, ["merge-base", baseRef, Head]);
        if (!result.Succeeded)
        {
            throw new GitCommandException($"cannot find the merge base of '{baseRef}' and {Head}: {result.Error.Trim()}");
        }

        return result.Output.Trim();
    }
}

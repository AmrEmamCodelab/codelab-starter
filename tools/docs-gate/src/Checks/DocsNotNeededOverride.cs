namespace Starter.Tools.DocsGate.Checks;

/// <summary>
/// Resolves the host-neutral override for the module-changed check. Sources, first non-empty wins:
/// 1. CODELAB_DOCS_NOT_NEEDED (the reason itself, exported by tools/build.ps1),
/// 2. a "Docs-Not-Needed: &lt;reason&gt;" line in CODELAB_PR_DESCRIPTION,
/// 3. a "Docs-Not-Needed: &lt;reason&gt;" line in the latest commit message.
/// A trailer without a reason is ignored: the reason is the point of the override.
/// </summary>
internal static class DocsNotNeededOverride
{
    public const string TrailerKey = "Docs-Not-Needed:";
    public const string ReasonEnvironmentVariable = "CODELAB_DOCS_NOT_NEEDED";
    public const string PrDescriptionEnvironmentVariable = "CODELAB_PR_DESCRIPTION";

    public static string? Resolve(Func<string, string?> readEnvironment, string? latestCommitMessage)
    {
        var reason = readEnvironment(ReasonEnvironmentVariable)?.Trim();
        if (!string.IsNullOrEmpty(reason))
        {
            return reason;
        }

        return FindTrailerReason(readEnvironment(PrDescriptionEnvironmentVariable))
            ?? FindTrailerReason(latestCommitMessage);
    }

    /// <summary>Returns the reason of the first Docs-Not-Needed line that has one, or null.</summary>
    public static string? FindTrailerReason(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith(TrailerKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var reason = line[TrailerKey.Length..].Trim();
            if (reason.Length > 0)
            {
                return reason;
            }
        }

        return null;
    }
}

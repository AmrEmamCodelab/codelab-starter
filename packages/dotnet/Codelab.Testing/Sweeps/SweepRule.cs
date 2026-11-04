using System.Text.RegularExpressions;

namespace Codelab.Testing.Sweeps;

/// <summary>One source-sweep rule.</summary>
/// <param name="Id">The rule id used in findings and in exception lists, for example <c>term-brand-product</c>.</param>
/// <param name="Pattern">The pattern, matched per line.</param>
/// <param name="Message">What a finding means and how to fix it.</param>
/// <param name="MatchNormalized">Also match the normalized line (identifiers split, Arabic normalized).</param>
/// <param name="RedactExcerpt">Hide the matched text in findings (secrets are never printed).</param>
public sealed record SweepRule(string Id, Regex Pattern, string Message, bool MatchNormalized = false, bool RedactExcerpt = false);

/// <summary>One match of a sweep rule.</summary>
/// <param name="RuleId">The rule that matched.</param>
/// <param name="RelativePath">The repo-relative file path, forward slashes.</param>
/// <param name="Line">The 1-based line number.</param>
/// <param name="Excerpt">The matched text, or a redacted form for rules that hide it.</param>
public sealed record SweepFinding(string RuleId, string RelativePath, int Line, string Excerpt)
{
    /// <summary><c>path:line: [rule] excerpt</c>.</summary>
    public override string ToString() => $"{RelativePath}:{Line}: [{RuleId}] {Excerpt}";
}

/// <summary>An exception entry: <paramref name="Glob"/> may match the listed rule ids.</summary>
/// <param name="Glob">A repo-relative glob (see <see cref="GlobPattern"/>).</param>
/// <param name="RuleIds">Exact ids, prefixes ending in <c>*</c>, or <c>*</c> for every rule.</param>
public sealed record SweepAllowance(string Glob, IReadOnlyList<string> RuleIds)
{
    private const string Wildcard = "*";

    /// <summary>True when this entry allows <paramref name="ruleId"/> to match in <paramref name="relativePath"/>.</summary>
    public bool Allows(string relativePath, string ruleId) =>
        GlobPattern.IsMatch(relativePath, Glob) && RuleIds.Any(selector => SelectorMatches(selector, ruleId));

    private static bool SelectorMatches(string selector, string ruleId)
    {
        if (selector == Wildcard)
        {
            return true;
        }

        return selector.EndsWith(Wildcard, StringComparison.Ordinal)
            ? ruleId.StartsWith(selector[..^Wildcard.Length], StringComparison.Ordinal)
            : string.Equals(selector, ruleId, StringComparison.Ordinal);
    }
}

using System.Text.RegularExpressions;

namespace Codelab.Testing.Sweeps;

/// <summary>
/// Built-in sweep rules: banned literals, brand identifiers and a compact set of secret shapes. Term rules come from
/// the term list via <see cref="SweepConfigurationFiles.ReadTermRules"/>.
/// </summary>
public static class SweepRules
{
    /// <summary>The id of the rule built by <see cref="BrandIdentifiers"/>.</summary>
    public const string BrandRuleId = "brand-id";

    /// <summary>The id prefix of the rules built by <see cref="Secrets"/>.</summary>
    public const string SecretRulePrefix = "secret-";

    private const string PemBoundary = "-----";
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>One rule matching any of <paramref name="literals"/> exactly (for example banned API names or old ids).</summary>
    public static SweepRule BannedLiterals(string ruleId, IEnumerable<string> literals, string message, bool ignoreCase = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        var alternatives = Alternatives(literals);
        var options = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        return new SweepRule(ruleId, new Regex(alternatives, options, MatchTimeout), message);
    }

    /// <summary>
    /// One case-insensitive rule matching any brand identifier as a word, also inside identifiers (normalized match),
    /// for example a client or product name that must not leak into a fork.
    /// </summary>
    public static SweepRule BrandIdentifiers(IEnumerable<string> brands)
    {
        var pattern = $@"(?<![\p{{L}}\p{{N}}])(?:{Alternatives(brands)})(?![\p{{L}}\p{{N}}])";
        var regex = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, MatchTimeout);
        return new SweepRule(BrandRuleId, regex, "brand identifier (use the project's own names)", MatchNormalized: true);
    }

    /// <summary>
    /// Secret shapes that must never be committed: private key blocks, cloud and repository tokens, JSON Web Tokens and
    /// passwords inside connection strings or URLs. Excerpts are redacted.
    /// </summary>
    public static IReadOnlyList<SweepRule> Secrets() =>
    [
        Secret("private-key", PemBoundary + @"BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY(?: BLOCK)?" + PemBoundary, "private key block"),
        Secret("aws-access-key", @"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b", "AWS access key id"),
        Secret("github-token", @"\bgh[pousr]_[A-Za-z0-9]{36,}\b", "GitHub token"),
        Secret("jwt", @"\beyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}", "JSON Web Token"),
        Secret("connection-string-password", @"\b(?:Password|Pwd)\s*=\s*(?!\{|\$|<)[^;""'\s]{4,}", "password inside a connection string"),
        Secret("url-credentials", @"\b[a-z][a-z0-9+.-]*://[^\s:@/""']+:[^\s@/""'{$<]{4,}@", "password embedded in a URL"),
    ];

    private static SweepRule Secret(string id, string pattern, string message) =>
        new(SecretRulePrefix + id, new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, MatchTimeout), message, RedactExcerpt: true);

    private static string Alternatives(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var escaped = values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(Regex.Escape).ToList();
        if (escaped.Count == 0)
        {
            throw new ArgumentException("At least one non-empty value is required.", nameof(values));
        }

        return string.Join('|', escaped);
    }
}

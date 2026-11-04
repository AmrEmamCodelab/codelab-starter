using System.Text.RegularExpressions;
using Codelab.Testing.Checks;

namespace Codelab.Testing.Sweeps;

/// <summary>
/// Runs sweep rules over the text files under a folder: banned literals, secrets, brand ids and source-product terms.
/// Path-parameterised, so a fork's test project sweeps its own tree with its own rule files.
/// </summary>
public static class SourceSweep
{
    private const char NullCharacter = '\0';
    private const string Redacted = "****";
    private const int RedactedVisibleCharacters = 4;

    /// <summary>Every finding, ordered by path and line.</summary>
    public static IReadOnlyList<SweepFinding> Run(SourceSweepOptions options, IEnumerable<SweepRule> rules)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(rules);
        var ruleList = rules.ToList();
        return [.. EnumerateFiles(options).SelectMany(file => SweepFile(options, file, ruleList))];
    }

    /// <summary>The sweep as a check: every finding is a violation; sweeping no file at all fails as vacuous.</summary>
    public static CheckResult Check(SourceSweepOptions options, IEnumerable<SweepRule> rules)
    {
        ArgumentNullException.ThrowIfNull(options);
        var files = EnumerateFiles(options).Count();
        var findings = Run(options, rules);
        return new CheckResult($"Source sweep of {options.Root}", findings.Select(finding => finding.ToString()), files);
    }

    /// <summary>The findings of <paramref name="rules"/> in one text, as if it were the file <paramref name="relativePath"/>.</summary>
    public static IReadOnlyList<SweepFinding> SweepText(string text, string relativePath, IEnumerable<SweepRule> rules, IEnumerable<SweepAllowance>? allowances = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(rules);
        var allowed = allowances?.ToList() ?? [];
        var path = GlobPattern.ToPosix(relativePath);
        var applicable = rules.Where(rule => !allowed.Exists(allowance => allowance.Allows(path, rule.Id))).ToList();
        var findings = new List<SweepFinding>();
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            findings.AddRange(SweepLine(lines[index].TrimEnd('\r'), path, index + 1, applicable));
        }

        return findings;
    }

    private static IEnumerable<SweepFinding> SweepFile(SourceSweepOptions options, string file, List<SweepRule> rules)
    {
        var text = File.ReadAllText(file);
        if (text.Contains(NullCharacter, StringComparison.Ordinal))
        {
            return [];
        }

        return SweepText(text, Path.GetRelativePath(options.Root, file), rules, options.Allowances);
    }

    private static IEnumerable<SweepFinding> SweepLine(string line, string path, int number, List<SweepRule> rules)
    {
        string? normalized = null;
        foreach (var rule in rules)
        {
            var match = rule.Pattern.Match(line);
            if (!match.Success && rule.MatchNormalized)
            {
                normalized ??= IdentifierNormalizer.Normalize(line);
                match = rule.Pattern.Match(normalized);
            }

            if (match.Success)
            {
                yield return new SweepFinding(rule.Id, path, number, Excerpt(rule, match));
            }
        }
    }

    private static string Excerpt(SweepRule rule, Match match)
    {
        if (!rule.RedactExcerpt)
        {
            return match.Value;
        }

        var visible = Math.Min(RedactedVisibleCharacters, match.Value.Length / RedactedVisibleCharacters);
        return match.Value[..visible] + Redacted;
    }

    private static IEnumerable<string> EnumerateFiles(SourceSweepOptions options)
    {
        var excludedDirectories = new HashSet<string>(options.ExcludedDirectoryNames, StringComparer.OrdinalIgnoreCase);
        var extensions = new HashSet<string>(options.IncludeExtensions, StringComparer.OrdinalIgnoreCase);
        return Walk(options.Root, excludedDirectories)
            .Where(file => extensions.Count == 0 || extensions.Contains(Path.GetExtension(file)))
            .Where(file => !options.ExcludedPaths.Any(glob => GlobPattern.IsMatch(Path.GetRelativePath(options.Root, file), glob)))
            .Where(file => new FileInfo(file).Length <= options.MaxFileBytes)
            .Order(StringComparer.Ordinal);
    }

    private static IEnumerable<string> Walk(string directory, HashSet<string> excludedDirectories)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            yield return file;
        }

        foreach (var child in Directory.EnumerateDirectories(directory).Where(child => !excludedDirectories.Contains(Path.GetFileName(child))))
        {
            foreach (var file in Walk(child, excludedDirectories))
            {
                yield return file;
            }
        }
    }
}

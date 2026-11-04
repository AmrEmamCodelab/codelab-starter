using System.Text.RegularExpressions;

namespace Codelab.Testing.Sweeps;

/// <summary>
/// Parsers for the two text files of the repository's scrub sweep, so a test sweep reads the same configuration: the
/// term list (<c>&lt;id&gt;[/i]: &lt;regex&gt;</c> per line) and the exception list (<c>&lt;glob&gt; &lt;ruleIds&gt;</c>).
/// Both live under <c>tools/extract/</c>; the caller passes their paths.
/// </summary>
public static partial class SweepConfigurationFiles
{
    /// <summary>The prefix every term rule id gets, as in the repository's sweep.</summary>
    public const string TermRulePrefix = "term-";

    private const char CommentMarker = '#';
    private const char RuleIdSeparator = ',';
    private const string CaseInsensitiveFlag = "i";
    private const string TermMessage = "source-product term (see the term list under tools/extract)";
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Reads a term list file into rules that also match normalized lines.</summary>
    public static IReadOnlyList<SweepRule> ReadTermRules(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ParseTermRules(File.ReadAllText(path), path);
    }

    /// <summary>Parses term list text; <paramref name="sourceName"/> names the input in error messages.</summary>
    /// <exception cref="FormatException">A line is not <c>&lt;id&gt;[/i]: &lt;regex&gt;</c>, or a regex does not compile.</exception>
    public static IReadOnlyList<SweepRule> ParseTermRules(string text, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(text);
        return [.. MeaningfulLines(text).Select(line => ParseTermLine(line.Text, $"{sourceName}:{line.Number}"))];
    }

    /// <summary>Reads an exception list file.</summary>
    public static IReadOnlyList<SweepAllowance> ReadExceptions(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ParseExceptions(File.ReadAllText(path), path);
    }

    /// <summary>Parses exception list text.</summary>
    /// <exception cref="FormatException">A line is not <c>&lt;glob&gt; &lt;ruleIds&gt;</c>.</exception>
    public static IReadOnlyList<SweepAllowance> ParseExceptions(string text, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(text);
        return [.. MeaningfulLines(text).Select(line => ParseExceptionLine(line.Text, $"{sourceName}:{line.Number}"))];
    }

    private static SweepRule ParseTermLine(string line, string location)
    {
        var match = TermLine().Match(line);
        if (!match.Success)
        {
            throw new FormatException($"{location}: expected \"<id>[/<flags>]: <regex>\", got \"{line}\".");
        }

        var flags = match.Groups["flags"].Value;
        if (flags.Length > 0 && flags != CaseInsensitiveFlag)
        {
            throw new FormatException($"{location}: unsupported flag \"{flags}\".");
        }

        var options = RegexOptions.CultureInvariant | (flags.Length > 0 ? RegexOptions.IgnoreCase : RegexOptions.None);
        try
        {
            var pattern = new Regex(match.Groups["pattern"].Value, options, MatchTimeout);
            return new SweepRule(TermRulePrefix + match.Groups["id"].Value, pattern, TermMessage, MatchNormalized: true);
        }
        catch (ArgumentException exception)
        {
            throw new FormatException($"{location}: invalid regex: {exception.Message}", exception);
        }
    }

    private static SweepAllowance ParseExceptionLine(string line, string location)
    {
        var match = ExceptionLine().Match(line);
        if (!match.Success)
        {
            throw new FormatException($"{location}: expected \"<glob> <ruleIds>\", got \"{line}\".");
        }

        var ruleIds = match.Groups["rules"].Value.Split(RuleIdSeparator, StringSplitOptions.RemoveEmptyEntries);
        return new SweepAllowance(match.Groups["glob"].Value, ruleIds);
    }

    private static IEnumerable<(string Text, int Number)> MeaningfulLines(string text) =>
        text.Split('\n')
            .Select((raw, index) => (Text: raw.Trim(), Number: index + 1))
            .Where(line => line.Text.Length > 0 && line.Text[0] != CommentMarker);

    [GeneratedRegex(@"^(?<id>[a-z0-9][a-z0-9-]*)(?:/(?<flags>[a-z]+))?:\s+(?<pattern>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex TermLine();

    [GeneratedRegex(@"^(?<glob>\S+)\s+(?<rules>\S+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ExceptionLine();
}

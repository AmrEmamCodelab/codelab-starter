using System.Text.RegularExpressions;

namespace Starter.Tools.DocsGate.Markdown;

/// <summary>A link or file reference found in Markdown.</summary>
/// <param name="Line">1-based line of the link.</param>
/// <param name="Target">The raw target as written (angle brackets removed).</param>
internal sealed record MarkdownLink(int Line, string Target);

/// <summary>
/// Extracts link targets from inline links and images (<c>[t](x)</c>, <c>![a](x)</c>), reference definitions
/// (<c>[id]: x</c>) and HTML <c>href</c>/<c>src</c> attributes. Code fences and inline code spans are ignored,
/// so examples in code never count as links.
/// </summary>
internal static partial class MarkdownLinkExtractor
{
    private const string TargetGroup = "target";
    private const char AngleOpen = '<';
    private const char AngleClose = '>';

    public static IEnumerable<MarkdownLink> Extract(string text)
    {
        foreach (var (line, lineText) in MarkdownProse.Lines(text))
        {
            var prose = InlineCodeRegex().Replace(lineText, string.Empty);
            foreach (var target in TargetsIn(prose))
            {
                yield return new MarkdownLink(line, target);
            }
        }
    }

    private static IEnumerable<string> TargetsIn(string prose) =>
        new[] { InlineLinkRegex(), ReferenceDefinitionRegex(), HtmlAttributeRegex() }
            .SelectMany(regex => regex.Matches(prose))
            .Select(match => Unwrap(match.Groups[TargetGroup].Value))
            .Where(target => target.Length > 0);

    private static string Unwrap(string target)
    {
        var trimmed = target.Trim();
        return trimmed.Length >= 2 && trimmed[0] == AngleOpen && trimmed[^1] == AngleClose
            ? trimmed[1..^1].Trim()
            : trimmed;
    }

    [GeneratedRegex(@"(?<ticks>`+).*?\k<ticks>")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"!?\[(?:[^\[\]]|\[[^\]]*\])*\]\(\s*(?<target><[^>]*>|[^\s)]+)(?:\s+(?:""[^""]*""|'[^']*'))?\s*\)")]
    private static partial Regex InlineLinkRegex();

    [GeneratedRegex(@"^ {0,3}\[[^\]]+\]:\s*(?<target><[^>]*>|\S+)")]
    private static partial Regex ReferenceDefinitionRegex();

    [GeneratedRegex(@"\b(?:href|src)\s*=\s*""(?<target>[^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlAttributeRegex();
}

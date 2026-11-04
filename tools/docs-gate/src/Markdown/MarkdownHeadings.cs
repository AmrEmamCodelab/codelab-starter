using System.Text.RegularExpressions;

namespace Starter.Tools.DocsGate.Markdown;

/// <summary>Reads ATX headings ("# Title" to "###### Title") outside code fences.</summary>
internal static partial class MarkdownHeadings
{
    public static IReadOnlyList<string> Read(string text) =>
        [.. MarkdownProse.Lines(text)
            .Select(line => HeadingRegex().Match(line.Text))
            .Where(match => match.Success)
            .Select(match => match.Groups["text"].Value.Trim())];

    [GeneratedRegex(@"^ {0,3}#{1,6}[ \t]+(?<text>.*?)(?:[ \t]+#+)?[ \t]*$")]
    private static partial Regex HeadingRegex();
}

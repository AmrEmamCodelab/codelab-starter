using System.Text.RegularExpressions;

namespace Starter.Tools.DocsGate.Markdown;

/// <summary>Yields the lines of a Markdown text that are prose, i.e. not inside a fenced code block.</summary>
internal static partial class MarkdownProse
{
    /// <summary>(1-based line number, line text without the line break) for every line outside code fences.</summary>
    public static IEnumerable<(int Line, string Text)> Lines(string text)
    {
        var lines = text.Split('\n');
        string? openFence = null;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            var fence = FenceRegex().Match(line);
            if (openFence is null)
            {
                if (fence.Success)
                {
                    openFence = fence.Groups["fence"].Value;
                    continue;
                }

                yield return (index + 1, line);
            }
            else if (IsClosingFence(fence, openFence))
            {
                openFence = null;
            }
        }
    }

    // A closing fence uses the same character, is at least as long as the opening one and has nothing after it.
    private static bool IsClosingFence(Match fence, string openFence)
    {
        var value = fence.Groups["fence"].Value;
        return fence.Success
            && value[0] == openFence[0]
            && value.Length >= openFence.Length
            && fence.Groups["info"].Value.Trim().Length == 0;
    }

    [GeneratedRegex(@"^ {0,3}(?<fence>`{3,}|~{3,})(?<info>.*)$")]
    private static partial Regex FenceRegex();
}

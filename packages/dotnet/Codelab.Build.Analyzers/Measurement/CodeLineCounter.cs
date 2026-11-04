using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Codelab.Build.Analyzers.Measurement;

/// <summary>
/// Counts the lines of a node that hold at least one token. Blank lines, comment-only lines, preprocessor directives
/// and disabled code are trivia, so they never count; a token spanning lines (a verbatim or raw string) counts each.
/// </summary>
internal static class CodeLineCounter
{
    public static int Count(SyntaxNode node)
    {
        var lines = node.SyntaxTree.GetText().Lines;
        var codeLines = new HashSet<int>();
        foreach (var token in node.DescendantTokens())
        {
            if (token.Span.IsEmpty || token.IsKind(SyntaxKind.EndOfFileToken))
            {
                continue;
            }

            AddLines(codeLines, lines.GetLinePositionSpan(token.Span));
        }

        return codeLines.Count;
    }

    private static void AddLines(HashSet<int> codeLines, LinePositionSpan span)
    {
        for (var line = span.Start.Line; line <= span.End.Line; line++)
        {
            codeLines.Add(line);
        }
    }
}

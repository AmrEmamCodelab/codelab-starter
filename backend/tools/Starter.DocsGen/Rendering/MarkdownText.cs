using System.Text;

namespace Starter.DocsGen.Rendering;

/// <summary>Markdown building blocks with deterministic output: LF line endings, escaped table cells, one trailing newline.</summary>
internal sealed class MarkdownText
{
    /// <summary>The only line ending DocsGen writes.</summary>
    public const char NewLine = '\n';

    private const string CellSeparator = " | ";
    private const string RowStart = "| ";
    private const string RowEnd = " |";
    private const string HeaderRule = "---";
    private const string Backtick = "`";

    private readonly StringBuilder text = new();

    /// <summary>Starts a document with its title and the generated-file notice.</summary>
    public static MarkdownText Document(string title)
    {
        var document = new MarkdownText();
        document.Line($"# {title}").Line().Line(GeneratedDocSet.MarkdownNotice).Line();
        return document;
    }

    /// <summary>Appends one line (or an empty one).</summary>
    public MarkdownText Line(string line = "")
    {
        text.Append(line).Append(NewLine);
        return this;
    }

    /// <summary>Appends a heading of <paramref name="level"/> followed by an empty line.</summary>
    public MarkdownText Heading(int level, string heading)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        return Line(new string('#', level) + " " + heading).Line();
    }

    /// <summary>Appends a table, then an empty line; <paramref name="emptyNote"/> replaces a table without rows.</summary>
    public MarkdownText Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, string emptyNote)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        var materialized = rows.ToList();
        if (materialized.Count == 0)
        {
            return Line(emptyNote).Line();
        }

        Row(headers).Row([.. headers.Select(_ => HeaderRule)]);
        foreach (var row in materialized)
        {
            Row(row);
        }

        return Line();
    }

    /// <summary>The text with exactly one trailing newline.</summary>
    public override string ToString() => text.ToString().TrimEnd(NewLine) + NewLine;

    /// <summary>A table cell: pipes escaped, line breaks folded into spaces, surrounding whitespace removed.</summary>
    public static string Cell(string? value) =>
        (value ?? string.Empty).Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace(NewLine, ' ')
            .Replace('\r', ' ')
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Trim();

    /// <summary>A code span (empty for an empty value).</summary>
    public static string Code(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : Backtick + value.Replace(Backtick, "'", StringComparison.Ordinal) + Backtick;

    private MarkdownText Row(IReadOnlyList<string> cells) =>
        Line(RowStart + string.Join(CellSeparator, cells.Select(Cell)) + RowEnd);
}

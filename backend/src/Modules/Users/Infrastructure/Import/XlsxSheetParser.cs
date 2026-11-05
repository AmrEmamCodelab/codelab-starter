using System.Globalization;
using System.Text;
using System.Xml;
using Starter.Modules.Users.Application.Import;

namespace Starter.Modules.Users.Infrastructure.Import;

/// <summary>
/// Reads the SpreadsheetML the import needs: the shared string table and the cells of a worksheet. Cell types: shared
/// string (<c>s</c>), inline string (<c>inlineStr</c>), formula string (<c>str</c>), and the raw value of every other
/// type (numbers come back as written, never reformatted). Phonetic runs are skipped. Elements are matched by local
/// name, so transitional and strict files both read.
/// </summary>
public static class XlsxSheetParser
{
    private const string SharedItemElement = "si";
    private const string TextElement = "t";
    private const string PhoneticElement = "rPh";
    private const string RowElement = "row";
    private const string CellElement = "c";
    private const string ValueElement = "v";
    private const string InlineStringElement = "is";
    private const string ReferenceAttribute = "r";
    private const string TypeAttribute = "t";
    private const string SharedStringType = "s";
    private const string InlineStringType = "inlineStr";
    private const int ColumnBase = 26;

    /// <summary>The shared strings of <paramref name="reader"/>, in order.</summary>
    /// <exception cref="SheetLimitExceededException">Too many strings, or one is too long.</exception>
    public static IReadOnlyList<string> SharedStrings(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var strings = new List<string>();
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != SharedItemElement)
            {
                continue;
            }

            if (strings.Count >= ImportLimits.MaxSharedStrings)
            {
                throw new SheetLimitExceededException("The workbook declares too many shared strings.");
            }

            strings.Add(ReadRichText(reader));
        }

        return strings;
    }

    /// <summary>The non-blank rows of the worksheet, in order; reading stops once <paramref name="maxRows"/> is passed.</summary>
    /// <param name="reader">The worksheet part.</param>
    /// <param name="sharedStrings">The shared strings.</param>
    /// <param name="maxRows">The most non-blank rows to return; one more ends the read with <see cref="SheetReadStatus.TooManyRows"/>.</param>
    public static (IReadOnlyList<SheetRow> Rows, bool Truncated) Rows(XmlReader reader, IReadOnlyList<string> sharedStrings, int maxRows)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(sharedStrings);
        var rows = new List<SheetRow>();
        var lastRowNumber = 0;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != RowElement)
            {
                continue;
            }

            lastRowNumber = NumberOf(reader.GetAttribute(ReferenceAttribute)) ?? lastRowNumber + 1;
            var row = new SheetRow(lastRowNumber, ReadCells(reader, sharedStrings));
            if (row.Cells.All(cell => cell is null))
            {
                continue;
            }

            if (rows.Count == maxRows)
            {
                return (rows, Truncated: true);
            }

            rows.Add(row);
        }

        return (rows, Truncated: false);
    }

    private static List<string?> ReadCells(XmlReader reader, IReadOnlyList<string> sharedStrings)
    {
        var cells = new List<string?>();
        if (reader.IsEmptyElement)
        {
            return cells;
        }

        var depth = reader.Depth;
        var nextColumn = 0;
        reader.Read();
        while (!reader.EOF && reader.Depth > depth)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == CellElement)
            {
                var column = ColumnOf(reader.GetAttribute(ReferenceAttribute)) ?? nextColumn;
                nextColumn = column + 1;
                var text = ReadCell(reader, sharedStrings);
                if (column < ImportLimits.MaxColumns)
                {
                    Place(cells, column, text);
                }
            }

            reader.Read();
        }

        return cells;
    }

    /// <summary>The text of the cell at <paramref name="reader"/>; leaves the reader on the cell's last node.</summary>
    private static string? ReadCell(XmlReader reader, IReadOnlyList<string> sharedStrings)
    {
        var type = reader.GetAttribute(TypeAttribute);
        if (reader.IsEmptyElement)
        {
            return null;
        }

        var depth = reader.Depth;
        string? text = null;
        reader.Read();
        while (!reader.EOF && reader.Depth > depth)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == ValueElement && type != InlineStringType)
            {
                text = ValueOf(reader.ReadElementContentAsString(), type, sharedStrings);
                continue;
            }

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == InlineStringElement)
            {
                text = ReadRichText(reader);
            }

            reader.Read();
        }

        return Clean(text);
    }

    private static string? ValueOf(string raw, string? type, IReadOnlyList<string> sharedStrings)
    {
        if (type != SharedStringType)
        {
            return raw;
        }

        return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < sharedStrings.Count
            ? sharedStrings[index]
            : throw new XmlException("A cell names a shared string that does not exist.");
    }

    /// <summary>
    /// The text of an <c>si</c> or <c>is</c> element: every <c>t</c> outside the phonetic runs, concatenated. Leaves the
    /// reader on the element's last node.
    /// </summary>
    private static string ReadRichText(XmlReader reader)
    {
        var text = new StringBuilder();
        if (reader.IsEmptyElement)
        {
            return string.Empty;
        }

        var depth = reader.Depth;
        reader.Read();
        while (!reader.EOF && reader.Depth > depth)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == PhoneticElement)
            {
                reader.Skip();
                continue;
            }

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == TextElement)
            {
                text.Append(reader.ReadElementContentAsString());
                RequireCellLength(text.Length);
                continue;
            }

            reader.Read();
        }

        return text.ToString();
    }

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        RequireCellLength(trimmed.Length);
        return trimmed;
    }

    private static void RequireCellLength(int length)
    {
        if (length > ImportLimits.MaxCellLength)
        {
            throw new SheetLimitExceededException("A cell is longer than the cell-length cap.");
        }
    }

    private static void Place(List<string?> cells, int column, string? text)
    {
        while (cells.Count <= column)
        {
            cells.Add(null);
        }

        cells[column] = text;
    }

    /// <summary>The 0-based column of a cell reference (<c>B3</c> is 1), or null when absent or malformed.</summary>
    internal static int? ColumnOf(string? reference)
    {
        if (string.IsNullOrEmpty(reference) || !char.IsAsciiLetter(reference[0]))
        {
            return null;
        }

        var column = 0;
        foreach (var character in reference.TakeWhile(char.IsAsciiLetter))
        {
            column = (column * ColumnBase) + (char.ToUpperInvariant(character) - 'A' + 1);
            if (column > ImportLimits.MaxColumns)
            {
                return ImportLimits.MaxColumns; // beyond the cap: ignored by the caller, and no overflow on long references
            }
        }

        return column - 1;
    }

    private static int? NumberOf(string? rowReference) =>
        int.TryParse(rowReference, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : null;
}

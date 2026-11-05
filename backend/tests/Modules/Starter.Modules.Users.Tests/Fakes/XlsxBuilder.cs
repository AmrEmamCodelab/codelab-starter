using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace Starter.Modules.Users.Tests.Fakes;

/// <summary>
/// Builds small .xlsx workbooks in memory with ZipArchive, the way a spreadsheet program lays them out (content types,
/// package and workbook relationships, one worksheet, optional shared strings), so the reader is tested on real
/// packages without any file on disk.
/// </summary>
internal sealed class XlsxBuilder
{
    private const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string TypeBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    private readonly List<string?[]> _rows = [];
    private readonly Dictionary<string, string> _extraEntries = new(StringComparer.Ordinal);

    public bool UseSharedStrings { get; set; } = true;

    public string SheetTarget { get; set; } = "worksheets/sheet1.xml";

    public string? RawSheetXml { get; set; }

    public static XlsxBuilder WithRows(params string?[][] rows)
    {
        var builder = new XlsxBuilder();
        builder._rows.AddRange(rows);
        return builder;
    }

    public XlsxBuilder Entry(string path, string content)
    {
        _extraEntries[path] = content;
        return this;
    }

    public byte[] Build()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var strings = new List<string>();
            Emit(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
            Emit(zip, "_rels/.rels", Relationships(("rId1", "officeDocument", "xl/workbook.xml")));
            Emit(zip, "xl/workbook.xml", $"<workbook xmlns=\"{MainNamespace}\" xmlns:r=\"{RelationshipNamespace}\"><sheets><sheet name=\"Users\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            var sheetPath = SheetTarget.StartsWith('/') ? SheetTarget.TrimStart('/') : "xl/" + SheetTarget;
            Emit(zip, sheetPath, RawSheetXml ?? Sheet(strings));
            var relationships = new List<(string, string, string)> { ("rId1", "worksheet", SheetTarget) };
            if (UseSharedStrings)
            {
                relationships.Add(("rId2", "sharedStrings", "sharedStrings.xml"));
                Emit(zip, "xl/sharedStrings.xml", SharedStrings(strings));
            }

            Emit(zip, "xl/_rels/workbook.xml.rels", Relationships([.. relationships]));
            foreach (var (path, content) in _extraEntries)
            {
                Write(zip, path, content);
            }
        }

        return buffer.ToArray();
    }

    public MemoryStream BuildStream() => new(Build());

    /// <summary>Writes a generated part unless the test supplied its own content for that path.</summary>
    private void Emit(ZipArchive zip, string path, string content)
    {
        if (!_extraEntries.ContainsKey(path))
        {
            Write(zip, path, content);
        }
    }

    private string Sheet(List<string> strings)
    {
        var xml = new StringBuilder($"<worksheet xmlns=\"{MainNamespace}\"><sheetData>");
        for (var rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            var rowNumber = rowIndex + 1;
            xml.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowNumber}\">");
            for (var column = 0; column < _rows[rowIndex].Length; column++)
            {
                if (_rows[rowIndex][column] is { } text)
                {
                    xml.Append(Cell(ColumnName(column) + rowNumber.ToString(CultureInfo.InvariantCulture), text, strings));
                }
            }

            xml.Append("</row>");
        }

        return xml.Append("</sheetData></worksheet>").ToString();
    }

    private string Cell(string reference, string text, List<string> strings)
    {
        if (!UseSharedStrings)
        {
            return $"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(text)}</t></is></c>";
        }

        strings.Add(text);
        return $"<c r=\"{reference}\" t=\"s\"><v>{(strings.Count - 1).ToString(CultureInfo.InvariantCulture)}</v></c>";
    }

    private static string SharedStrings(List<string> strings) =>
        $"<sst xmlns=\"{MainNamespace}\" count=\"{strings.Count}\">" + string.Concat(strings.Select(text => $"<si><t>{SecurityElement.Escape(text)}</t></si>")) + "</sst>";

    private static string Relationships(params (string Id, string Type, string Target)[] relationships) =>
        $"<Relationships xmlns=\"{PackageRelationshipNamespace}\">"
        + string.Concat(relationships.Select(relationship => $"<Relationship Id=\"{relationship.Id}\" Type=\"{TypeBase}{relationship.Type}\" Target=\"{relationship.Target}\"/>"))
        + "</Relationships>";

    public static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var value = index + 1; value > 0; value = (value - 1) / 26)
        {
            name = (char)('A' + ((value - 1) % 26)) + name;
        }

        return name;
    }

    public static void Write(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }
}

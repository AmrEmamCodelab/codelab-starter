using System.IO.Compression;
using System.Text;
using Starter.Modules.Users.Application.Import;
using Starter.Modules.Users.Infrastructure.Import;
using Starter.Modules.Users.Tests.Fakes;

namespace Starter.Modules.Users.Tests.Infrastructure;

public sealed class XlsxSheetReaderTests
{
    private const int MaxRows = 50;
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private readonly XlsxSheetReader _reader = new();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reads_the_header_and_the_rows_through_shared_or_inline_strings(bool sharedStrings)
    {
        var builder = XlsxBuilder.WithRows(["UserName", "Kind"], ["amal", "internal"], [" badr ", "external"]);
        builder.UseSharedStrings = sharedStrings;

        var result = _reader.Read(builder.BuildStream(), MaxRows);

        Assert.Equal(SheetReadStatus.Read, result.Status);
        Assert.Equal(["UserName", "Kind"], result.Header!.Cells);
        Assert.Equal([2, 3], result.Rows.Select(row => row.RowNumber));
        Assert.Equal(["badr", "external"], result.Rows[1].Cells);
    }

    [Fact]
    public void Blank_rows_are_skipped_sparse_cells_keep_their_column_and_numbers_stay_as_written()
    {
        var sheet = $"<worksheet xmlns=\"{Main}\"><sheetData>"
            + "<row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>UserName</t></is></c><c r=\"C2\" t=\"inlineStr\"><is><t>Code</t></is></c></row>"
            + "<row r=\"3\"><c r=\"A3\"/></row>"
            + "<row r=\"5\"><c r=\"C5\"><v>0042</v></c><c t=\"inlineStr\"><is><t>next</t></is></c></row>"
            + "</sheetData></worksheet>";

        var result = _reader.Read(new XlsxBuilder { RawSheetXml = sheet, UseSharedStrings = false }.BuildStream(), MaxRows);

        Assert.Equal(2, result.Header!.RowNumber);
        var row = Assert.Single(result.Rows);
        Assert.Equal(5, row.RowNumber);
        Assert.Equal([null, null, "0042", "next"], row.Cells);
    }

    [Fact]
    public void Rich_text_runs_are_joined_and_phonetic_runs_skipped()
    {
        var sharedStrings = $"<sst xmlns=\"{Main}\"><si><r><t>Us</t></r><r><t xml:space=\"preserve\">er</t></r><rPh><t>x</t></rPh><r><t>Name</t></r></si></sst>";
        var sheet = $"<worksheet xmlns=\"{Main}\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row></sheetData></worksheet>";
        var builder = new XlsxBuilder { RawSheetXml = sheet }.Entry("xl/sharedStrings.xml", sharedStrings);

        var result = _reader.Read(builder.BuildStream(), MaxRows);

        Assert.Equal(["UserName"], result.Header!.Cells);
    }

    [Fact]
    public void A_sheet_reached_through_an_absolute_relationship_target_is_read()
    {
        var builder = XlsxBuilder.WithRows(["UserName"], ["amal"]);
        builder.SheetTarget = "/xl/worksheets/data.xml";

        Assert.Equal(SheetReadStatus.Read, _reader.Read(builder.BuildStream(), MaxRows).Status);
    }

    [Fact]
    public void More_rows_than_allowed_is_too_many_rows()
    {
        var result = _reader.Read(XlsxBuilder.WithRows(["UserName"], ["a"], ["b"], ["c"]).BuildStream(), maxDataRows: 2);

        Assert.Equal(SheetReadStatus.TooManyRows, result.Status);
    }

    [Fact]
    public void Exactly_the_allowed_rows_is_fine() =>
        Assert.Equal(SheetReadStatus.Read, _reader.Read(XlsxBuilder.WithRows(["UserName"], ["a"], ["b"]).BuildStream(), maxDataRows: 2).Status);

    [Fact]
    public void An_empty_sheet_is_empty() =>
        Assert.Equal(SheetReadStatus.Empty, _reader.Read(XlsxBuilder.WithRows().BuildStream(), MaxRows).Status);

    [Fact]
    public void A_cell_longer_than_the_cap_is_too_large() =>
        Assert.Equal(
            SheetReadStatus.TooLarge,
            _reader.Read(XlsxBuilder.WithRows([new string('x', ImportLimits.MaxCellLength + 1)]).BuildStream(), MaxRows).Status);

    [Fact]
    public void Columns_beyond_the_cap_are_ignored()
    {
        var wide = new string?[ImportLimits.MaxColumns + 2];
        wide[0] = "UserName";
        wide[^1] = "far";

        var result = _reader.Read(XlsxBuilder.WithRows(wide).BuildStream(), MaxRows);

        Assert.Equal(["UserName"], result.Header!.Cells);
    }

    [Fact]
    public void Too_many_zip_entries_is_too_large()
    {
        var builder = XlsxBuilder.WithRows(["UserName"]);
        for (var index = 0; index <= ImportLimits.MaxZipEntries; index++)
        {
            builder.Entry($"xl/media/pad{index}.xml", "<x/>");
        }

        Assert.Equal(SheetReadStatus.TooLarge, _reader.Read(builder.BuildStream(), MaxRows).Status);
    }

    [Fact]
    public void A_zip_bomb_is_refused_before_it_is_inflated()
    {
        // A few kilobytes on the wire, more than the uncompressed cap once inflated.
        var bomb = XlsxBuilder.WithRows(["UserName"]).Entry("xl/media/bomb.xml", new string(' ', (int)ImportLimits.MaxUncompressedBytes + 1));
        var bytes = bomb.Build();

        Assert.True(bytes.Length < ImportLimits.MaxUploadBytes);
        Assert.Equal(SheetReadStatus.TooLarge, _reader.Read(new MemoryStream(bytes), MaxRows).Status);
    }

    [Fact]
    public void An_upload_past_the_cap_is_too_large() =>
        Assert.Equal(SheetReadStatus.TooLarge, _reader.Read(new MemoryStream(new byte[ImportLimits.MaxUploadBytes + 1]), MaxRows).Status);

    [Theory]
    [InlineData("not a zip at all")]
    [InlineData("")]
    public void A_file_that_is_not_a_zip_is_invalid(string content) =>
        Assert.Equal(SheetReadStatus.Invalid, _reader.Read(new MemoryStream(Encoding.UTF8.GetBytes(content)), MaxRows).Status);

    [Fact]
    public void Malformed_xml_is_invalid() =>
        Assert.Equal(SheetReadStatus.Invalid, _reader.Read(new XlsxBuilder { RawSheetXml = "<worksheet><sheetData><row>" }.BuildStream(), MaxRows).Status);

    [Fact]
    public void A_dtd_is_refused()
    {
        var sheet = "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e \"boom\">]><worksheet><sheetData/></worksheet>";

        Assert.Equal(SheetReadStatus.Invalid, _reader.Read(new XlsxBuilder { RawSheetXml = sheet }.BuildStream(), MaxRows).Status);
    }

    [Fact]
    public void A_shared_string_index_out_of_range_is_invalid()
    {
        var sheet = $"<worksheet xmlns=\"{Main}\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>9</v></c></row></sheetData></worksheet>";

        Assert.Equal(SheetReadStatus.Invalid, _reader.Read(new XlsxBuilder { RawSheetXml = sheet }.BuildStream(), MaxRows).Status);
    }

    [Fact]
    public void A_zip_without_a_workbook_is_invalid()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            XlsxBuilder.Write(zip, "readme.txt", "hello");
        }

        Assert.Equal(SheetReadStatus.Invalid, _reader.Read(new MemoryStream(buffer.ToArray()), MaxRows).Status);
    }

    [Theory]
    [InlineData("A1", 0)]
    [InlineData("C7", 2)]
    [InlineData("Z1", 25)]
    [InlineData("AA1", 26)]
    [InlineData("1A", null)]
    [InlineData(null, null)]
    public void Column_references(string? reference, int? expected) => Assert.Equal(expected, XlsxSheetParser.ColumnOf(reference));

    [Fact]
    public void A_very_long_column_reference_does_not_overflow() =>
        Assert.Equal(ImportLimits.MaxColumns, XlsxSheetParser.ColumnOf(new string('Z', 40) + "1"));
}

public sealed class BudgetedStreamTests
{
    [Fact]
    public void Reading_past_the_budget_throws_the_limit_exception()
    {
        var budget = new ReadBudget(limit: 10);
        using var stream = new BudgetedStream(new MemoryStream(new byte[11]), budget);
        var buffer = new byte[4];

        Assert.Equal(4, stream.Read(buffer, 0, 4));
        Assert.Equal(4, stream.Read(buffer, 0, 4));
        Assert.Throws<SheetLimitExceededException>(() => stream.Read(buffer, 0, 4));
    }

    [Fact]
    public void The_budget_is_shared_between_parts()
    {
        var budget = new ReadBudget(limit: 6);
        using var first = new BudgetedStream(new MemoryStream(new byte[4]), budget);
        using var second = new BudgetedStream(new MemoryStream(new byte[4]), budget);
        var buffer = new byte[4];

        Assert.Equal(4, first.Read(buffer, 0, 4));

        Assert.Throws<SheetLimitExceededException>(() => second.Read(buffer, 0, 4));
    }

    [Fact]
    public void The_stream_is_read_only_and_not_seekable()
    {
        using var stream = new BudgetedStream(new MemoryStream(), new ReadBudget(1));

        Assert.True(stream.CanRead);
        Assert.False(stream.CanSeek || stream.CanWrite);
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.Write([1], 0, 1));
        Assert.Throws<NotSupportedException>(() => stream.Length);
    }
}

namespace Starter.Modules.Users.Application.Import;

/// <summary>The outcome of reading a workbook.</summary>
public enum SheetReadStatus
{
    /// <summary>The first worksheet was read.</summary>
    Read = 1,

    /// <summary>The file is not a readable .xlsx workbook (not a zip, no worksheet, malformed XML).</summary>
    Invalid = 2,

    /// <summary>A size cap was exceeded (entries; uncompressed bytes; columns; cell length; shared strings).</summary>
    TooLarge = 3,

    /// <summary>The sheet has more data rows than allowed.</summary>
    TooManyRows = 4,

    /// <summary>The first worksheet has no row with any text (not even a header).</summary>
    Empty = 5,
}

/// <summary>One sheet row as text cells.</summary>
/// <param name="RowNumber">The row number shown in Excel (1 is the first row).</param>
/// <param name="Cells">The trimmed cell texts by column (0 is column A); null for a blank or missing cell.</param>
public sealed record SheetRow(int RowNumber, IReadOnlyList<string?> Cells)
{
    /// <summary>The trimmed text of column <paramref name="index"/>, or null when blank or beyond the row.</summary>
    public string? CellAt(int index) => index >= 0 && index < Cells.Count ? Cells[index] : null;
}

/// <summary>What the reader got from the first worksheet.</summary>
/// <param name="Status">Read, or why not.</param>
/// <param name="Header">The first row with text (the header), or null unless <see cref="SheetReadStatus.Read"/>.</param>
/// <param name="Rows">The non-blank rows after the header, in sheet order.</param>
public sealed record SheetReadResult(SheetReadStatus Status, SheetRow? Header, IReadOnlyList<SheetRow> Rows)
{
    /// <summary>A failed read.</summary>
    public static SheetReadResult Failed(SheetReadStatus status) => new(status, Header: null, Rows: []);
}

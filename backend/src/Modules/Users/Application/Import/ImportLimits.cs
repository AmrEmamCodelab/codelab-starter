namespace Starter.Modules.Users.Application.Import;

/// <summary>
/// The hard caps of the Excel import. A workbook is a zip of XML parts, so every cap that bounds work is checked while
/// reading, not trusted from the file's own headers: the upload size, the number of zip entries, the uncompressed
/// bytes read, the rows, the columns and the cell length (the zip-bomb guard). The tunable values
/// (<see cref="ImportSettings"/>) stay within these.
/// </summary>
public static class ImportLimits
{
    /// <summary>The file extension the import accepts.</summary>
    public const string FileExtension = ".xlsx";

    /// <summary>The largest upload, in bytes (5 MiB).</summary>
    public const long MaxUploadBytes = 5L * 1024 * 1024;

    /// <summary>The most entries the workbook zip may have (a sheet export has about a dozen).</summary>
    public const int MaxZipEntries = 256;

    /// <summary>The most uncompressed bytes read from the parts the reader opens, in total (32 MiB).</summary>
    public const long MaxUncompressedBytes = 32L * 1024 * 1024;

    /// <summary>The most columns a row may use.</summary>
    public const int MaxColumns = 64;

    /// <summary>The longest cell text.</summary>
    public const int MaxCellLength = 1024;

    /// <summary>The most shared strings the workbook may declare.</summary>
    public const int MaxSharedStrings = 100_000;

    /// <summary>The highest data-row limit a deployment may configure.</summary>
    public const int MaxRowsCeiling = 5_000;

    /// <summary>The default data-row limit.</summary>
    public const int DefaultMaxRows = 1_000;

    /// <summary>The default lifetime of a previewed batch, in minutes.</summary>
    public const int DefaultBatchLifetimeMinutes = 30;

    /// <summary>The longest lifetime a deployment may configure, in minutes (one day).</summary>
    public const int MaxBatchLifetimeMinutes = 1_440;
}

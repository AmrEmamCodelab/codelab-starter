using System.IO.Compression;
using System.Xml;
using Starter.Modules.Users.Application;
using Starter.Modules.Users.Application.Import;

namespace Starter.Modules.Users.Infrastructure.Import;

/// <summary>
/// The minimal .xlsx reader of the user import: System.IO.Compression and XmlReader only (no spreadsheet package).
/// It reads the first worksheet's cells as text through the shared strings and inline strings, and enforces every
/// <see cref="ImportLimits"/> cap while it reads: the upload size, the zip entries (count and declared size), the bytes
/// actually inflated, the shared strings, the columns, the cell length and the rows. A bad file is a status, never an
/// exception: an unreadable zip or malformed XML is <see cref="SheetReadStatus.Invalid"/>, a broken cap is
/// <see cref="SheetReadStatus.TooLarge"/>.
/// </summary>
public sealed class XlsxSheetReader : IUserSheetReader
{
    /// <inheritdoc />
    public SheetReadResult Read(Stream content, int maxDataRows)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDataRows);
        try
        {
            using var buffered = Buffer(content);
            using var archive = new ZipArchive(buffered, ZipArchiveMode.Read, leaveOpen: false);
            return ReadArchive(archive, maxDataRows);
        }
        catch (SheetLimitExceededException)
        {
            return SheetReadResult.Failed(SheetReadStatus.TooLarge);
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or NotSupportedException)
        {
            // Not a zip, a corrupt entry or malformed XML. The reader's message may quote cell contents, so it is not logged.
            return SheetReadResult.Failed(SheetReadStatus.Invalid);
        }
    }

    private static SheetReadResult ReadArchive(ZipArchive archive, int maxDataRows)
    {
        if (archive.Entries.Count > ImportLimits.MaxZipEntries
            || archive.Entries.Sum(entry => entry.Length) > ImportLimits.MaxUncompressedBytes)
        {
            return SheetReadResult.Failed(SheetReadStatus.TooLarge);
        }

        var package = new XlsxPackage(archive, new ReadBudget(ImportLimits.MaxUncompressedBytes));
        var sheetPath = package.FirstSheetPath();
        if (sheetPath is null)
        {
            return SheetReadResult.Failed(SheetReadStatus.Invalid);
        }

        var sharedStrings = ReadSharedStrings(package);
        using var sheet = package.Open(sheetPath);
        if (sheet is null)
        {
            return SheetReadResult.Failed(SheetReadStatus.Invalid);
        }

        // One extra row is allowed for the header, which is the first non-blank row.
        var (rows, truncated) = XlsxSheetParser.Rows(sheet, sharedStrings, maxDataRows + 1);
        if (truncated)
        {
            return SheetReadResult.Failed(SheetReadStatus.TooManyRows);
        }

        return rows.Count == 0
            ? SheetReadResult.Failed(SheetReadStatus.Empty)
            : new SheetReadResult(SheetReadStatus.Read, rows[0], [.. rows.Skip(1)]);
    }

    private static IReadOnlyList<string> ReadSharedStrings(XlsxPackage package)
    {
        var path = package.SharedStringsPath();
        using var reader = path is null ? null : package.Open(path);
        return reader is null ? [] : XlsxSheetParser.SharedStrings(reader);
    }

    /// <summary>A seekable copy of the upload (the zip directory is at its end), refused past the upload cap.</summary>
    private static MemoryStream Buffer(Stream content)
    {
        var buffered = new MemoryStream();
        var chunk = new byte[ushort.MaxValue];
        int read;
        while ((read = content.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffered.Length + read > ImportLimits.MaxUploadBytes)
            {
                buffered.Dispose();
                throw new SheetLimitExceededException("The upload is larger than the upload cap.");
            }

            buffered.Write(chunk, 0, read);
        }

        buffered.Position = 0;
        return buffered;
    }
}

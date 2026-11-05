using Starter.Application.Results;
using Starter.Application.Validation;

namespace Starter.Modules.Users.Application.Import;

/// <summary>An uploaded import file, as the endpoint received it.</summary>
/// <param name="Content">The file's bytes, or null when the request carried no file.</param>
/// <param name="Length">The declared length in bytes.</param>
/// <param name="FileName">The file name the browser sent, or null.</param>
public sealed record ImportUpload(Stream? Content, long Length, string? FileName);

/// <summary>A previewed import: the stored batch and every row's verdict.</summary>
/// <param name="BatchId">The batch to commit.</param>
/// <param name="Rows">Every data row with its errors (empty for a valid row).</param>
/// <param name="ValidCount">The number of valid rows.</param>
/// <param name="InvalidCount">The number of rows with an error.</param>
public sealed record ImportPreview(long BatchId, IReadOnlyList<CheckedImportRow> Rows, int ValidCount, int InvalidCount);

/// <summary>
/// <c>POST /api/admin/user-import/preview</c>: reads one .xlsx (first worksheet, header-driven columns, capped by
/// <see cref="ImportLimits"/> and <see cref="ImportSettings.MaxRows"/>), validates every row and stores the batch for a
/// commit. Nothing is created yet. The sheet has no password column: password-bound rows get activation links at commit.
/// </summary>
public sealed class PreviewUserImport(IUserSheetReader reader, ImportRowValidator validator, ImportBatches batches, ImportSettings settings)
{
    /// <summary>
    /// The preview, or a refusal (400): <see cref="UsersErrorCodes.ImportFileRequired"/>, <see cref="UsersErrorCodes.ImportFileInvalid"/>,
    /// <see cref="UsersErrorCodes.ImportFileTooLarge"/>, <see cref="UsersErrorCodes.ImportTooManyRows"/>,
    /// <see cref="UsersErrorCodes.ImportNoRows"/>, or <see cref="UsersErrorCodes.ImportColumnsMissing"/> with one field
    /// error (<see cref="ValidationMessages.Required"/>) per missing column.
    /// </summary>
    public async Task<Outcome<ImportPreview>> ExecuteAsync(ImportUpload upload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upload);
        if (UploadRefusal(upload) is { } refusal)
        {
            return refusal;
        }

        var sheet = reader.Read(upload.Content!, settings.MaxRows);
        if (SheetRefusal(sheet) is { } sheetRefusal)
        {
            return sheetRefusal;
        }

        var map = ImportColumns.Map(sheet.Header!);
        if (map.Missing.Count > 0)
        {
            return Refusal.Invalid(MissingColumnErrors(map), UsersErrorCodes.ImportColumnsMissing);
        }

        var rows = await validator.ValidateAsync([.. sheet.Rows.Select(row => ImportColumns.RowOf(map, row))], cancellationToken);
        var batch = await batches.SavePreviewAsync(rows, cancellationToken);
        var valid = rows.Count(row => row.IsValid);
        return new ImportPreview(batch.Id, rows, ValidCount: valid, InvalidCount: rows.Count - valid);
    }

    private static Refusal? UploadRefusal(ImportUpload upload)
    {
        if (upload.Content is null || upload.Length <= 0)
        {
            return Refusal.Invalid(UsersErrorCodes.ImportFileRequired);
        }

        if (upload.Length > ImportLimits.MaxUploadBytes)
        {
            return Refusal.Invalid(UsersErrorCodes.ImportFileTooLarge);
        }

        return string.Equals(Path.GetExtension(upload.FileName), ImportLimits.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? null
            : Refusal.Invalid(UsersErrorCodes.ImportFileInvalid);
    }

    private static Refusal? SheetRefusal(SheetReadResult sheet) => sheet.Status switch
    {
        SheetReadStatus.Read when sheet.Rows.Count == 0 => Refusal.Invalid(UsersErrorCodes.ImportNoRows),
        SheetReadStatus.Read => null,
        SheetReadStatus.Empty => Refusal.Invalid(UsersErrorCodes.ImportNoRows),
        SheetReadStatus.TooLarge => Refusal.Invalid(UsersErrorCodes.ImportFileTooLarge),
        SheetReadStatus.TooManyRows => Refusal.Invalid(UsersErrorCodes.ImportTooManyRows),
        _ => Refusal.Invalid(UsersErrorCodes.ImportFileInvalid),
    };

    private static Dictionary<string, string[]> MissingColumnErrors(ImportColumnMap map) =>
        map.Missing.ToDictionary(column => FieldNames.Of(column.Header), _ => new[] { ValidationMessages.Required }, StringComparer.Ordinal);
}

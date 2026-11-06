using System.Diagnostics.CodeAnalysis;

namespace Codelab.Security.Uploads;

/// <summary>
/// The one MIME allowlist for uploads: the content type of every extension <see cref="FileSignatureSniffer"/> knows.
/// The attachment service stamps it on stored files and downloads, and the API uses the same table, so the two can
/// never disagree about what a <c>.pdf</c> is. An extension without a known signature has no content type: it was
/// never accepted, so it is never served.
/// </summary>
public static class UploadContentTypes
{
    /// <summary>The content type of a PDF document.</summary>
    public const string Pdf = "application/pdf";

    /// <summary>The content type of a PNG image.</summary>
    public const string Png = "image/png";

    /// <summary>The content type of a JPEG image (<c>.jpg</c> and <c>.jpeg</c>).</summary>
    public const string Jpeg = "image/jpeg";

    /// <summary>The content type of a zip archive.</summary>
    public const string Zip = "application/zip";

    /// <summary>The content type by extension (lower case, with the dot); one entry per <see cref="FileSignatureSniffer.KnownSignatures"/> key.</summary>
    public static readonly IReadOnlyDictionary<string, string> ByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = Pdf,
        [".png"] = Png,
        [".jpg"] = Jpeg,
        [".jpeg"] = Jpeg,
        [".zip"] = Zip,
    };

    /// <summary>
    /// The content type of <paramref name="fileNameOrExtension"/> (a file name such as <c>scan.PDF</c>, or an extension
    /// such as <c>.pdf</c>), judged on the normalised extension (<see cref="FileNames.NormalizedExtension"/>); false
    /// for an unknown extension, no extension or an alternate-data-stream suffix. Never throws.
    /// </summary>
    public static bool TryGetContentType(string? fileNameOrExtension, [NotNullWhen(true)] out string? contentType)
    {
        contentType = null;
        if (string.IsNullOrWhiteSpace(fileNameOrExtension))
        {
            return false;
        }

        var extension = FileNames.NormalizedExtension(fileNameOrExtension);
        return extension.Length > 0 && ByExtension.TryGetValue(extension, out contentType);
    }
}

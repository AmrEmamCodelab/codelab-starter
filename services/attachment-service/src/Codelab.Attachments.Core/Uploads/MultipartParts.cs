using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Codelab.Attachments.Core.Uploads;

/// <summary>Reads the multipart framing: the boundary of the request and the client file name of a <c>file</c> part.</summary>
internal static class MultipartParts
{
    /// <summary>The form part name every uploaded file must use.</summary>
    internal const string FilePartName = "file";

    /// <summary>The only request media type accepted.</summary>
    internal const string FormDataMediaType = "multipart/form-data";

    /// <summary>The longest boundary RFC 2046 allows.</summary>
    internal const int MaxBoundaryLength = 70;

    /// <summary>The boundary of a <c>multipart/form-data</c> content type; false for any other type or a missing or oversized boundary.</summary>
    internal static bool TryReadBoundary(string? contentType, out string boundary)
    {
        boundary = string.Empty;
        if (!MediaTypeHeaderValue.TryParse(contentType, out var mediaType)
            || !string.Equals(mediaType.MediaType.Value, FormDataMediaType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxBoundaryLength)
        {
            return false;
        }

        boundary = value;
        return true;
    }

    /// <summary>
    /// The client file name of a <c>form-data; name="file"; filename=...</c> part (<c>filename*</c> wins); false for
    /// any other part. The name is untrusted: it is only ever reduced with <c>FileNames.SafeLeafName</c>.
    /// </summary>
    internal static bool TryReadFileName(MultipartSection section, out string fileName)
    {
        fileName = string.Empty;
        if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
            || !disposition.IsFileDisposition()
            || !string.Equals(HeaderUtilities.RemoveQuotes(disposition.Name).Value, FilePartName, StringComparison.Ordinal))
        {
            return false;
        }

        var name = disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName;
        fileName = HeaderUtilities.RemoveQuotes(name).Value ?? string.Empty;
        return true;
    }
}

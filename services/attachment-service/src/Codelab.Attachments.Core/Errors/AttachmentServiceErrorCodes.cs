namespace Codelab.Attachments.Core.Errors;

/// <summary>
/// The machine codes the attachment service answers with (problem+json <c>code</c>). The client renders
/// <c>errors.&lt;CODE&gt;</c> in Arabic and English, so a refusal carries no sentence.
/// </summary>
public static class AttachmentServiceErrorCodes
{
    /// <summary>401: the upload ticket is missing, expired, already used or not for uploads.</summary>
    public const string TicketInvalid = "ATTACHMENTS_TICKET_INVALID";

    /// <summary>413: a file passed the per-file cap, or the request passed the total cap.</summary>
    public const string FileTooLarge = "ATTACHMENTS_FILE_TOO_LARGE";

    /// <summary>413: more file parts than the per-request cap.</summary>
    public const string TooManyFiles = "ATTACHMENTS_TOO_MANY_FILES";

    /// <summary>415: the request is not multipart, a part is not a file, or the extension is blocked or not allowed.</summary>
    public const string TypeNotAllowed = "ATTACHMENTS_TYPE_NOT_ALLOWED";

    /// <summary>415: the content does not start with the magic bytes of its extension.</summary>
    public const string SignatureMismatch = "ATTACHMENTS_SIGNATURE_MISMATCH";

    /// <summary>400: no file part, an empty file, or a multipart body that cannot be read.</summary>
    public const string EmptyFile = "ATTACHMENTS_EMPTY_FILE";

    /// <summary>404: no stored file has this id.</summary>
    public const string FileNotFound = "ATTACHMENTS_FILE_NOT_FOUND";
}

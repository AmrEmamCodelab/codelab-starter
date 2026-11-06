using Starter.Application.Attachments;

namespace Starter.Modules.Attachments.Application;

/// <summary>
/// The Attachments module's error codes. The client renders <c>errors.&lt;CODE&gt;</c> from its ar and en bundles.
/// The attachment service's own refusals (<c>ATTACHMENTS_TICKET_INVALID</c>, <c>ATTACHMENTS_FILE_TOO_LARGE</c>, ...)
/// are owned by <c>services/attachment-service</c>.
/// </summary>
public static class AttachmentsErrorCodes
{
    /// <summary>The upload receipt does not verify, was issued for another record, type or user, or names a file already linked elsewhere (400).</summary>
    public const string ReceiptInvalid = "ATTACHMENTS_RECEIPT_INVALID";

    /// <summary>No record type with this key takes attachments (404).</summary>
    public const string RecordTypeUnknown = "ATTACHMENTS_RECORD_TYPE_UNKNOWN";

    /// <summary>The record type has no active attachment type with this key (404).</summary>
    public const string TypeNotFound = "ATTACHMENTS_TYPE_NOT_FOUND";

    /// <summary>The file's extension is not one the attachment type allows (400).</summary>
    public const string ExtensionNotAllowedForType = "ATTACHMENTS_EXTENSION_NOT_ALLOWED_FOR_TYPE";

    /// <summary>The record already holds the most files the attachment type allows (409).</summary>
    public const string MaxFilesReached = "ATTACHMENTS_MAX_FILES_REACHED";

    /// <summary>
    /// No such attachment, or no such record, or one the caller may not see or change (404: the same answer, so
    /// existence never leaks).
    /// </summary>
    public const string NotFound = "ATTACHMENTS_NOT_FOUND";

    /// <summary>A required attachment type has no attachment yet (409); the kernel constant, re-exported for the generated docs.</summary>
    public const string RequiredMissing = AttachmentErrorCodes.RequiredMissing;

    /// <summary>The attachment service could not be reached (503, thrown as <see cref="AttachmentStorageUnavailableException"/>).</summary>
    public const string StorageUnavailable = "ATTACHMENTS_STORAGE_UNAVAILABLE";

    // scaffold:error-codes
}

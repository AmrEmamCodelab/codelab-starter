namespace Starter.Application.Records;

/// <summary>
/// Why a submit or resubmit was refused. Mapped to the wire once, by <see cref="SubmitRefusals.ToRefusal"/>; never
/// switch on it in a controller.
/// </summary>
public enum SubmitRefusal
{
    /// <summary>The record does not exist (404 <see cref="RecordsErrorCodes.NotFound"/>).</summary>
    NotFound = 1,

    /// <summary>The record is not the caller's (404 <see cref="RecordsErrorCodes.NotFound"/>, so its existence is not confirmed).</summary>
    NotOwner = 2,

    /// <summary>The record's current status does not allow a submit (409 <see cref="RecordsErrorCodes.NotSubmittable"/>).</summary>
    NotSubmittable = 3,

    /// <summary>The record's fields fail the submit check (400 <c>VALIDATION_FAILED</c> with every field error).</summary>
    ValidationFailed = 4,

    /// <summary>A required attachment type has no file (409 <c>ATTACHMENTS_REQUIRED_MISSING</c>).</summary>
    AttachmentsMissing = 5,

    /// <summary>The record changed under the caller (409 <see cref="RecordsErrorCodes.Concurrency"/>).</summary>
    Concurrency = 6,
}

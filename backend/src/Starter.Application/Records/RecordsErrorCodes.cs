namespace Starter.Application.Records;

/// <summary>
/// The error codes of the Records kernel refusals (<see cref="SubmitRefusals"/>). The client renders
/// <c>errors.&lt;CODE&gt;</c> from its ar and en bundles. Validation and the attachment interlock reuse
/// <c>PlatformErrorCodes.Validation</c> and <c>AttachmentErrorCodes.RequiredMissing</c>.
/// </summary>
public static class RecordsErrorCodes
{
    /// <summary>The record does not exist or is not the caller's (404; a foreign record is never confirmed to exist).</summary>
    public const string NotFound = "RECORDS_NOT_FOUND";

    /// <summary>The record is not in a state that can be submitted or resubmitted (409).</summary>
    public const string NotSubmittable = "RECORDS_NOT_SUBMITTABLE";

    /// <summary>Someone else changed the record first; reload and try again (409).</summary>
    public const string Concurrency = "RECORDS_CONCURRENCY";
}

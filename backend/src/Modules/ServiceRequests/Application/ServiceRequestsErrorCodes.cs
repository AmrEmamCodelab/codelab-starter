namespace Starter.Modules.ServiceRequests.Application;

/// <summary>
/// The ServiceRequests module's error codes. The client renders <c>errors.&lt;CODE&gt;</c> from its ar and en bundles.
/// The record-level refusals (not found, not submittable, concurrency) are the Records kernel's
/// (<c>Starter.Application.Records.RecordsErrorCodes</c>); the field codes are <see cref="Validation.ServiceRequestValidationCodes"/>.
/// </summary>
public static class ServiceRequestsErrorCodes
{
    /// <summary>The request is no longer a draft or returned, so its fields cannot change (409).</summary>
    public const string NotEditable = "SERVICE_REQUESTS_NOT_EDITABLE";

    // scaffold:error-codes
}

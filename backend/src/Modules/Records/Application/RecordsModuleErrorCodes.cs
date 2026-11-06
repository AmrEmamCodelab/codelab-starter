namespace Starter.Modules.Records.Application;

/// <summary>
/// The Records module's own error codes, beside the kernel's <c>Starter.Application.Records.RecordsErrorCodes</c>
/// (RECORDS_NOT_FOUND, RECORDS_NOT_SUBMITTABLE, RECORDS_CONCURRENCY), which this module returns for everything else.
/// The client renders <c>errors.&lt;CODE&gt;</c> from its ar and en bundles.
/// </summary>
public static class RecordsModuleErrorCodes
{
    /// <summary>The token names no active account, so nobody can own or act on a record (403).</summary>
    public const string ActorUnknown = "RECORDS_ACTOR_UNKNOWN";

    /// <summary>The caller lacks the record type's view-own permission, so has no "my records" list (403).</summary>
    public const string ViewForbidden = "RECORDS_VIEW_FORBIDDEN";

    // scaffold:error-codes
}

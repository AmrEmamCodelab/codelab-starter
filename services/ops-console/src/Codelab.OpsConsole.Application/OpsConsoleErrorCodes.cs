namespace Codelab.OpsConsole.Application;

/// <summary>
/// The Ops Console's own problem+json <c>code</c> values. Authentication, authorization and upstream failures reuse the
/// Codelab.Errors codes (<c>UNAUTHENTICATED</c>, <c>FORBIDDEN</c>, <c>UPSTREAM_UNAVAILABLE</c>).
/// </summary>
public static class OpsConsoleErrorCodes
{
    /// <summary>The input is neither a reference code (<c>9C1E5-B7A20</c>) nor a 32-hex correlation id.</summary>
    public const string ReferenceCodeInvalid = "OPS_REFERENCE_CODE_INVALID";

    /// <summary>Neither the API nor the log store knows the reference code.</summary>
    public const string ReferenceCodeNotFound = "OPS_REFERENCE_CODE_NOT_FOUND";
}

namespace Starter.Modules.Ops.Application;

/// <summary>
/// The Ops module's error codes. The Ops Console reads them (the staff SPA never calls <c>/api/ops</c>); the console
/// owns the text.
/// </summary>
public static class OpsErrorCodes
{
    /// <summary>400: the call did not carry a usable operator name (<c>X-Codelab-Operator</c>).</summary>
    public const string OperatorRequired = "OPS_OPERATOR_REQUIRED";

    /// <summary>400: the reference code is not two groups of five hex characters joined by a hyphen.</summary>
    public const string ReferenceCodeInvalid = "OPS_REFERENCE_CODE_INVALID";

    /// <summary>404: no error response with that reference code is on record (never seen, or older than the retention).</summary>
    public const string ReferenceCodeNotFound = "OPS_REFERENCE_CODE_NOT_FOUND";

    // scaffold:error-codes
}

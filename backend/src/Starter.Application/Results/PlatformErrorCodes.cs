namespace Starter.Application.Results;

/// <summary>
/// The platform error codes a kernel refusal can carry without a module code. They mirror the Codelab.Errors
/// vocabulary (this assembly references no package); a kernel test fails when a value drifts from it.
/// </summary>
public static class PlatformErrorCodes
{
    /// <summary>Well-formed request, invalid field values (Codelab.Errors <c>ErrorCodes.Validation</c>).</summary>
    public const string Validation = "VALIDATION_FAILED";
}

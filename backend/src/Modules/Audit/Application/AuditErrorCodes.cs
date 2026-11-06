namespace Starter.Modules.Audit.Application;

/// <summary>The Audit module's error codes. The client renders <c>errors.&lt;CODE&gt;</c> from its ar and en bundles.</summary>
public static class AuditErrorCodes
{
    /// <summary>The audit filter is not valid; the field errors say which value (400).</summary>
    public const string InvalidFilter = "AUDIT_INVALID_FILTER";

    // scaffold:error-codes
}

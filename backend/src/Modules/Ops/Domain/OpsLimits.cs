namespace Starter.Modules.Ops.Domain;

/// <summary>The Ops module's size limits: validators and the column sizes use the same constants.</summary>
public static class OpsLimits
{
    /// <summary>The longest operator name the Ops Console may forward (a <c>DOMAIN\account</c> or a UPN fits).</summary>
    public const int OperatorMaxLength = 256;

    /// <summary>The longest calling service name (the assertion's issuer).</summary>
    public const int ServiceNameMaxLength = 64;

    /// <summary>The longest correlation id (Codelab.Observability accepts up to 64 characters from outside).</summary>
    public const int CorrelationIdMaxLength = 64;

    /// <summary>The length of a reference code: two groups of five hex characters and a hyphen (<c>9C1E5-B7A20</c>).</summary>
    public const int ReferenceCodeLength = 11;

    /// <summary>The longest HTTP method recorded.</summary>
    public const int MethodMaxLength = 16;

    /// <summary>The longest request path recorded; a longer one is cut to this length.</summary>
    public const int PathMaxLength = 512;

    /// <summary>The longest subject recorded for an operator call (a reference code fits).</summary>
    public const int SubjectMaxLength = 64;
}

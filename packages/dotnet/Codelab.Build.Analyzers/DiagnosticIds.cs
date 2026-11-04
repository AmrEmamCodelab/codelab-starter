namespace Codelab.Build.Analyzers;

/// <summary>Diagnostic ids of the Codelab clean-code analyzers (ADR-012).</summary>
public static class DiagnosticIds
{
    /// <summary>A method, constructor, operator or accessor body has more code lines than codelab_method_lines.</summary>
    public const string MethodTooLong = "CLB1001";

    /// <summary>A source file has more code lines than codelab_file_lines.</summary>
    public const string FileTooLong = "CLB1002";

    /// <summary>A method, constructor, delegate or primary constructor has more parameters than codelab_parameters.</summary>
    public const string TooManyParameters = "CLB1003";

    /// <summary>A numeric literal outside the allowed numbers and outside a naming context (const, enum, attribute...).</summary>
    public const string MagicNumber = "CLB1004";

    /// <summary>An enum member without an explicit value (enums are stored as integers).</summary>
    public const string EnumMemberWithoutValue = "CLB1005";
}

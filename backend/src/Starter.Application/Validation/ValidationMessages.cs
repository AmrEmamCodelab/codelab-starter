namespace Starter.Application.Validation;

/// <summary>
/// The per-field validation message codes. The client translates them (<c>validation.&lt;CODE&gt;</c> in the ar and en
/// bundles); the server never writes a sentence.
/// </summary>
public static class ValidationMessages
{
    /// <summary>The field is missing or blank.</summary>
    public const string Required = "REQUIRED";

    /// <summary>The field is longer than allowed.</summary>
    public const string TooLong = "TOO_LONG";

    /// <summary>The field does not have the expected shape.</summary>
    public const string InvalidFormat = "INVALID_FORMAT";
}

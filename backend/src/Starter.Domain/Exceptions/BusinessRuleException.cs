namespace Starter.Domain.Exceptions;

/// <summary>
/// A business rule was broken where no result can be returned (ERROR-HANDLING row 1). The host maps it to 422 with
/// <see cref="Code"/>; an expected "no" that a use case can return is a refusal instead, never this exception.
/// The message is logged, never sent: keep it free of personal data.
/// </summary>
public sealed class BusinessRuleException : Exception
{
    /// <summary>Creates the exception with the module error code the client branches on.</summary>
    public BusinessRuleException(string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    /// <summary>The module error code (a constant of the module's <c>ErrorCodes</c> class).</summary>
    public string Code { get; }
}

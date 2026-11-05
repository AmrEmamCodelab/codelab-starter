namespace Starter.Application.Validation;

/// <summary>
/// The validator of one request body (rule <c>base.endpoint-contract</c>: every write has one; the contract tests find
/// them through this interface). Validators are pure: they never throw and return every field error at once.
/// </summary>
/// <typeparam name="T">The request body type an endpoint binds.</typeparam>
public interface IValidator<in T>
{
    /// <summary>Every field error of <paramref name="input"/>; empty when it is valid.</summary>
    ValidationErrors Validate(T input);
}

using Starter.Application.Validation;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application;

/// <summary>The body of <c>POST /api/auth/login</c>.</summary>
/// <param name="UserName">The user name.</param>
/// <param name="Password">The password.</param>
public sealed record LoginCommand(string? UserName, string? Password);

/// <summary>The body of <c>POST /api/auth/refresh</c>.</summary>
/// <param name="RefreshToken">The refresh token from the login or the previous refresh.</param>
public sealed record RefreshCommand(string? RefreshToken);

/// <summary>Validates <see cref="LoginCommand"/>: both fields present and within their limits.</summary>
public sealed class LoginCommandValidator : IValidator<LoginCommand>
{
    /// <inheritdoc />
    public ValidationErrors Validate(LoginCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new ValidationErrors()
            .RequireText(FieldNames.Of(nameof(LoginCommand.UserName)), input.UserName, IdentityLimits.UserNameMaxLength)
            .RequireText(FieldNames.Of(nameof(LoginCommand.Password)), input.Password, IdentityLimits.PasswordMaxLength);
    }
}

/// <summary>Validates <see cref="RefreshCommand"/>: the token is present and within its limit.</summary>
public sealed class RefreshCommandValidator : IValidator<RefreshCommand>
{
    /// <inheritdoc />
    public ValidationErrors Validate(RefreshCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new ValidationErrors()
            .RequireText(FieldNames.Of(nameof(RefreshCommand.RefreshToken)), input.RefreshToken, IdentityLimits.RefreshTokenMaxLength);
    }
}

/// <summary>A signed-in session as the client receives it.</summary>
/// <param name="AccessToken">The access token (bearer).</param>
/// <param name="AccessTokenExpiresAt">When the access token expires.</param>
/// <param name="RefreshToken">The refresh token (rotated on every refresh).</param>
/// <param name="IdleTimeoutMinutes">The idle timeout the client enforces, in minutes.</param>
/// <param name="UserId">The internal user id (the Api layer sends it as a PublicId).</param>
/// <param name="DisplayName">The user's display name.</param>
public sealed record SignedInSession(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    int IdleTimeoutMinutes,
    long UserId,
    string DisplayName);

using Microsoft.Extensions.Options;
using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Infrastructure.Security;

namespace Starter.Modules.Identity.Infrastructure.Options;

/// <summary>Start-up validation of <see cref="SessionOptions"/>: positive lifetimes, access token within the idle timeout.</summary>
public sealed class SessionOptionsValidator : IValidateOptions<SessionOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.AccessTokenMinutes <= 0 || options.RefreshTokenDays <= 0 || options.IdleTimeoutMinutes <= 0)
        {
            return ValidateOptionsResult.Fail(
                $"{SessionOptions.SectionName}: {nameof(options.AccessTokenMinutes)}, {nameof(options.RefreshTokenDays)} and {nameof(options.IdleTimeoutMinutes)} must be positive.");
        }

        return options.AccessTokenMinutes > options.IdleTimeoutMinutes
            ? ValidateOptionsResult.Fail(
                $"{SessionOptions.SectionName}:{nameof(options.AccessTokenMinutes)} must not exceed {nameof(options.IdleTimeoutMinutes)} (an access token would outlive an idle session).")
            : ValidateOptionsResult.Success;
    }
}

/// <summary>Start-up validation of <see cref="JwtOptions"/>: a base64 signing key of at least 32 bytes.</summary>
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return SigningKey.TryDecode(options.SigningKey, out _)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{JwtOptions.SectionName}:{nameof(options.SigningKey)} is required: base64 of at least {JwtOptions.MinimumSigningKeyBytes} random bytes, from the local secrets file (tools/devseed/New-LocalSecrets.ps1) or the environment.");
    }
}

/// <summary>
/// Start-up validation of <see cref="BreakGlassOptions"/>: a user name, and a strong password in every environment
/// (at least <see cref="IdentityLimits.BreakGlassPasswordMinLength"/> characters, of which at least
/// <see cref="IdentityLimits.BreakGlassPasswordMinDistinctCharacters"/> distinct, and at most
/// <see cref="IdentityLimits.PasswordMaxLength"/>, the longest a login accepts).
/// </summary>
public sealed class BreakGlassOptionsValidator : IValidateOptions<BreakGlassOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, BreakGlassOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.UserName) || options.UserName.Length > IdentityLimits.UserNameMaxLength)
        {
            return ValidateOptionsResult.Fail($"{BreakGlassOptions.SectionName}:{nameof(options.UserName)} is required (at most {IdentityLimits.UserNameMaxLength} characters).");
        }

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            return ValidateOptionsResult.Fail(
                $"{BreakGlassOptions.SectionName}:{nameof(options.Password)} is required: set it in the local secrets file (tools/devseed/New-LocalSecrets.ps1) or the environment. No default exists.");
        }

        return IsStrong(options.Password)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{BreakGlassOptions.SectionName}:{nameof(options.Password)} is too weak: use {IdentityLimits.BreakGlassPasswordMinLength} to {IdentityLimits.PasswordMaxLength} characters with at least {IdentityLimits.BreakGlassPasswordMinDistinctCharacters} different ones (tools/devseed/New-LocalSecrets.ps1 generates one).");
    }

    private static bool IsStrong(string password) =>
        password.Length is >= IdentityLimits.BreakGlassPasswordMinLength and <= IdentityLimits.PasswordMaxLength
        && password.Distinct().Count() >= IdentityLimits.BreakGlassPasswordMinDistinctCharacters;
}

/// <summary>Start-up validation of <see cref="DevUsersOptions"/>: every entry has a unique user name and a password.</summary>
public sealed class DevUsersOptionsValidator : IValidateOptions<DevUsersOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DevUsersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < options.Users.Count; index++)
        {
            var user = options.Users[index];
            if (string.IsNullOrWhiteSpace(user.UserName) || string.IsNullOrWhiteSpace(user.Password))
            {
                failures.Add($"{DevUsersOptions.SectionName}:{index}: {nameof(user.UserName)} and {nameof(user.Password)} are required.");
            }
            else if (!seen.Add(UserNames.Normalize(user.UserName)))
            {
                failures.Add($"{DevUsersOptions.SectionName}:{index}: the user name '{user.UserName}' is repeated.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

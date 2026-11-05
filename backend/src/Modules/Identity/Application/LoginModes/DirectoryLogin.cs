using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Modules.Identity.Application.Auditing;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application.LoginModes;

/// <summary>The body of <c>POST /api/auth/ad/login</c>.</summary>
/// <param name="UserName">The directory user name as the user types it (<c>user</c>, <c>DOMAIN\user</c> or <c>user@domain</c>).</param>
/// <param name="Password">The directory password.</param>
public sealed record DirectoryLoginCommand(string? UserName, string? Password)
{
    /// <summary>A log-safe description: the password is never printed.</summary>
    public override string ToString() => nameof(DirectoryLoginCommand);
}

/// <summary>Validates <see cref="DirectoryLoginCommand"/>: both fields present and within their limits.</summary>
public sealed class DirectoryLoginCommandValidator : IValidator<DirectoryLoginCommand>
{
    /// <inheritdoc />
    public ValidationErrors Validate(DirectoryLoginCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new ValidationErrors()
            .RequireText(FieldNames.Of(nameof(DirectoryLoginCommand.UserName)), input.UserName, IdentityLimits.UserNameMaxLength)
            .RequireText(FieldNames.Of(nameof(DirectoryLoginCommand.Password)), input.Password, IdentityLimits.PasswordMaxLength);
    }
}

/// <summary>What the directory said about a user name and password. Logged as the integer value.</summary>
public enum DirectoryVerdictStatus
{
    /// <summary>The directory accepted the password.</summary>
    Accepted = 1,

    /// <summary>The directory refused the user name or password.</summary>
    Refused = 2,

    /// <summary>The directory account is disabled, expired or locked.</summary>
    AccountDisabled = 3,

    /// <summary>The directory could not be asked; no password was checked.</summary>
    Unavailable = 4,
}

/// <summary>The directory's verdict and, when accepted, the value the local account is matched on.</summary>
/// <param name="Status">The verdict.</param>
/// <param name="MatchValue">The directory's value of <see cref="IDirectorySignIn.MatchField"/>, or null (absent or not accepted).</param>
public sealed record DirectoryVerdict(DirectoryVerdictStatus Status, string? MatchValue)
{
    /// <summary>A log-safe description: the match value is never printed.</summary>
    public override string ToString() => nameof(DirectoryVerdict) + "(" + Status + ")";
}

/// <summary>The Active Directory, as the directory login needs it (implemented over Codelab.Identity).</summary>
public interface IDirectorySignIn
{
    /// <summary>The local account field a directory identity is matched on (<c>Codelab:Identity:ActiveDirectory:MatchBy</c>).</summary>
    AccountMatchField MatchField { get; }

    /// <summary>Asks the directory whether <paramref name="password"/> is <paramref name="userName"/>'s.</summary>
    Task<DirectoryVerdict> VerifyAsync(string userName, string password, CancellationToken cancellationToken);
}

/// <summary>
/// Signs in with a directory user name and password: the directory checks the password, the identity it returns is
/// matched to an existing directory-bound account (<see cref="SignInExternalAccount"/>), and the one session issuer
/// starts an Active Directory session. A refused password, a disabled directory account, a missing match value and an
/// unknown, inactive or wrongly bound local account all answer <see cref="IdentityErrorCodes.InvalidCredentials"/>; an
/// unreachable directory answers <see cref="LoginModeErrorCodes.DirectoryUnavailable"/> (no password was checked, so
/// it is not a credential verdict). Identity's lockout does not apply: the directory owns the password and its lockout.
/// </summary>
public sealed class DirectoryLoginUseCase(
    IValidator<DirectoryLoginCommand> validator,
    IDirectorySignIn directory,
    SignInExternalAccount signIn,
    IAuthEventSink audit)
{
    /// <summary>The signed-in session, or a refusal.</summary>
    public async Task<Outcome<LoginResult>> ExecuteAsync(DirectoryLoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        var verdict = await directory.VerifyAsync(userName: command.UserName!, password: command.Password!, cancellationToken);
        if (verdict is { Status: DirectoryVerdictStatus.Accepted, MatchValue: { } matchValue } && !string.IsNullOrWhiteSpace(matchValue))
        {
            return await signIn.ExecuteAsync(directory.MatchField, value: matchValue, SignInMode.ActiveDirectory, idTokenHint: null, cancellationToken);
        }

        await audit.RecordAsync(
            new AuthEventRecord(
                AuthEventType.LoginFailed,
                UserName: command.UserName,
                Mode: SignInMode.ActiveDirectory,
                Reason: ExternalFailureReasons.Of(verdict.Status)),
            cancellationToken);
        return verdict.Status == DirectoryVerdictStatus.Unavailable
            ? Refusal.Unprocessable(LoginModeErrorCodes.DirectoryUnavailable)
            : Refusal.Invalid(IdentityErrorCodes.InvalidCredentials);
    }
}

/// <summary>
/// The one mapping from an external authority's refusal to the auth trail's <see cref="AuthFailureReason"/>. A
/// refusal the trail has no member for yet (the directory or the identity provider could not be asked, an SSO
/// callback that failed before any identity was known) is recorded with no reason.
/// </summary>
public static class ExternalFailureReasons
{
    /// <summary>The reason recorded for a directory verdict that started no session.</summary>
    public static AuthFailureReason? Of(DirectoryVerdictStatus status) => status switch
    {
        DirectoryVerdictStatus.Refused => AuthFailureReason.WrongPassword,
        DirectoryVerdictStatus.AccountDisabled => AuthFailureReason.AccountInactive,
        DirectoryVerdictStatus.Accepted => AuthFailureReason.UnknownAccount,
        _ => null,
    };
}

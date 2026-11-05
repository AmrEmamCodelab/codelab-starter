using Starter.Application.Results;
using Starter.Application.Users;
using Starter.Application.Validation;
using Starter.Modules.Identity.Application.Auditing;
using Starter.Modules.Identity.Application.LoginModes;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application;

/// <summary>
/// <c>POST /api/auth/login</c>: checks a user name and password (<see cref="PasswordCheck"/>: lockout, binding,
/// activation) and starts a basic session. Every refusal is the same <see cref="IdentityErrorCodes.InvalidCredentials"/>,
/// also for a locked account with the right password; the auth event keeps the real reason. While <c>Auth:Modes</c>
/// does not list basic, only the break-glass administrator can sign in here (<see cref="BasicLoginAccounts"/>), and every
/// refusal is 404 <see cref="LoginModeErrorCodes.LoginModeDisabled"/>, as for any disabled mode, so the closed endpoint
/// does not reveal that the break-glass path exists or which user name it has.
/// </summary>
public sealed class LoginUseCase(
    IValidator<LoginCommand> validator,
    BasicLoginAccounts accounts,
    PasswordCheck passwords,
    SessionIssuer issuer,
    IAuthEventSink audit)
{
    /// <summary>
    /// The signed-in session, or a refusal (<see cref="PlatformErrorCodes.Validation"/>, <see cref="IdentityErrorCodes.InvalidCredentials"/>;
    /// <see cref="LoginModeErrorCodes.LoginModeDisabled"/> for every refusal while basic is off).
    /// </summary>
    public async Task<Outcome<LoginResult>> ExecuteAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return accounts.IsModeOpen ? Refusal.Invalid(errors) : Refusal.NotFound(LoginModeErrorCodes.LoginModeDisabled);
        }

        var target = await accounts.FindAsync(UserNames.Normalize(command.UserName!), cancellationToken);
        var check = await passwords.CheckAsync(target.Account, command.Password!, cancellationToken);
        if (!check.IsPassed)
        {
            await RecordRefusalAsync(target, command.UserName!, check, cancellationToken);
            return target.IsModeOpen
                ? Refusal.Invalid(IdentityErrorCodes.InvalidCredentials)
                : Refusal.NotFound(LoginModeErrorCodes.LoginModeDisabled);
        }

        var account = target.Account!;
        var session = await issuer.StartAsync(account, SessionStart.Basic, cancellationToken);
        await audit.RecordAsync(
            new AuthEventRecord(AuthEventType.LoginSucceeded, UserId: account.Id, UserName: command.UserName, Mode: SignInMode.Basic),
            cancellationToken);
        return session;
    }

    private async Task RecordRefusalAsync(BasicLoginTarget target, string userName, PasswordCheckResult check, CancellationToken cancellationToken)
    {
        var reason = target.ExcludedUserId is null ? check.Reason : AuthFailureReason.LoginModeDisabled;
        await audit.RecordAsync(
            new AuthEventRecord(AuthEventType.LoginFailed, UserId: target.Account?.Id ?? target.ExcludedUserId, UserName: userName, Mode: SignInMode.Basic, Reason: reason),
            cancellationToken);
        if (check.StartedLockout)
        {
            await audit.RecordAsync(
                new AuthEventRecord(AuthEventType.AccountLockedOut, UserId: target.Account?.Id, UserName: userName, Mode: SignInMode.Basic, Reason: AuthFailureReason.WrongPassword),
                cancellationToken);
        }
    }
}

/// <summary>
/// <c>POST /api/auth/refresh</c>: rotates the refresh token of an open session and issues a new access token. A refresh
/// token that was already rotated is treated as stolen and ends the session; so does a deactivated account or a
/// session version that moved on (deactivation, password reset, activation). Every refusal records an auth event.
/// </summary>
public sealed class RefreshSessionUseCase(
    IValidator<RefreshCommand> validator,
    IUserSessionRepository sessions,
    IUserAccountRepository accounts,
    SessionIssuer issuer,
    IAuthEventSink audit)
{
    /// <summary>The rotated session, or a refusal (<see cref="PlatformErrorCodes.Validation"/>, <see cref="IdentityErrorCodes.RefreshRejected"/>).</summary>
    public async Task<Outcome<LoginResult>> ExecuteAsync(RefreshCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        if (!RefreshToken.TryParse(command.RefreshToken, out var token)
            || await sessions.FindBySessionIdAsync(token.SessionId, cancellationToken) is not { } session)
        {
            return await RejectAsync(userId: null, AuthFailureReason.RefreshTokenInvalid, cancellationToken);
        }

        var decision = session.CheckRefresh(token.Secret, issuer.Now(), issuer.Settings.IdleTimeout);
        if (decision != RefreshDecision.Allowed)
        {
            return await RefuseAsync(session, endSession: decision == RefreshDecision.SecretMismatch, ReasonOf(decision), cancellationToken);
        }

        var user = await accounts.FindByIdAsync(session.UserId, cancellationToken);
        if (user is not { IsActive: true } || !await issuer.IsCurrentAsync(user, session, cancellationToken))
        {
            return await RefuseAsync(session, endSession: true, AuthFailureReason.SessionVersionStale, cancellationToken);
        }

        return await issuer.RotateAsync(user, session, cancellationToken);
    }

    private static AuthFailureReason ReasonOf(RefreshDecision decision) => decision switch
    {
        RefreshDecision.Revoked => AuthFailureReason.SessionEnded,
        RefreshDecision.Expired => AuthFailureReason.SessionExpired,
        RefreshDecision.Idle => AuthFailureReason.SessionIdle,
        _ => AuthFailureReason.RefreshTokenReused,
    };

    private async Task<Outcome<LoginResult>> RefuseAsync(
        UserSession session, bool endSession, AuthFailureReason reason, CancellationToken cancellationToken)
    {
        if (endSession)
        {
            await issuer.EndAsync(session, cancellationToken);
        }

        return await RejectAsync(session.UserId, reason, cancellationToken);
    }

    private async Task<Outcome<LoginResult>> RejectAsync(long? userId, AuthFailureReason reason, CancellationToken cancellationToken)
    {
        await audit.RecordAsync(new AuthEventRecord(AuthEventType.RefreshRejected, UserId: userId, Reason: reason), cancellationToken);
        return Refusal.Invalid(IdentityErrorCodes.RefreshRejected);
    }
}

/// <summary>
/// <c>POST /api/auth/logout</c>: ends the caller's session (only that one: the user's other sessions keep working).
/// Its refresh token stops working and its session id is revoked, so the access token in hand is refused (401) on the
/// next request. Records an auth event.
/// </summary>
public sealed class LogoutUseCase(ICurrentUserContext currentUser, IUserSessionRepository sessions, SessionIssuer issuer, IAuthEventSink audit)
{
    /// <summary>Done, or <see cref="IdentityErrorCodes.SessionRequired"/> when the token names no session.</summary>
    public async Task<Outcome<Done>> ExecuteAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.SessionId))
        {
            return Refusal.Forbidden(IdentityErrorCodes.SessionRequired);
        }

        var session = await sessions.FindBySessionIdAsync(currentUser.SessionId, cancellationToken);
        if (session is null)
        {
            await issuer.RevokeSessionIdAsync(currentUser.SessionId, cancellationToken);
        }
        else
        {
            await issuer.EndAsync(session, cancellationToken);
        }

        await audit.RecordAsync(new AuthEventRecord(AuthEventType.Logout, UserId: session?.UserId, Mode: session?.SignInMode), cancellationToken);
        return Done.Value;
    }
}

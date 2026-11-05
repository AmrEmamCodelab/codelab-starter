using Starter.Application.Results;
using Starter.Application.Users;
using Starter.Application.Validation;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application;

/// <summary>
/// <c>POST /api/auth/login</c>: checks a user name and password and starts a session. Every credential failure (unknown
/// user, wrong password, deactivated account) is the same refusal, and an unknown user costs the same hashing work.
/// </summary>
public sealed class LoginUseCase(
    IValidator<LoginCommand> validator,
    IUserAccountRepository accounts,
    IPasswordHasher hasher,
    SessionIssuer issuer)
{
    /// <summary>The signed-in session, or a refusal (<see cref="PlatformErrorCodes.Validation"/>, <see cref="IdentityErrorCodes.InvalidCredentials"/>).</summary>
    public async Task<Outcome<SignedInSession>> ExecuteAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        var user = await accounts.FindByUserNameAsync(UserNames.Normalize(command.UserName!), cancellationToken);
        var verification = hasher.Verify(user?.PasswordHash, command.Password!);
        if (user is null || !user.IsActive || verification == PasswordVerification.Failed)
        {
            return Refusal.Invalid(IdentityErrorCodes.InvalidCredentials);
        }

        if (verification == PasswordVerification.SucceededRehashNeeded)
        {
            user.ReplacePasswordHash(hasher.Hash(command.Password!));
        }

        return await issuer.StartAsync(user, cancellationToken);
    }
}

/// <summary>
/// <c>POST /api/auth/refresh</c>: rotates the refresh token of an open session and issues a new access token. A refresh
/// token that was already rotated is treated as stolen and ends the session; so does a deactivated account or a
/// session version that moved on (password change, deactivation, permission change).
/// </summary>
public sealed class RefreshSessionUseCase(
    IValidator<RefreshCommand> validator,
    IUserSessionRepository sessions,
    IUserAccountRepository accounts,
    ISessionVersions versions,
    SessionIssuer issuer)
{
    /// <summary>The rotated session, or a refusal (<see cref="PlatformErrorCodes.Validation"/>, <see cref="IdentityErrorCodes.RefreshRejected"/>).</summary>
    public async Task<Outcome<SignedInSession>> ExecuteAsync(RefreshCommand command, CancellationToken cancellationToken)
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
            return Rejected();
        }

        var decision = session.CheckRefresh(token.Secret, issuer.Now(), issuer.Settings.IdleTimeout);
        if (decision != RefreshDecision.Allowed)
        {
            return await RefuseAsync(session, endSession: decision == RefreshDecision.SecretMismatch, cancellationToken);
        }

        var user = await accounts.FindByIdAsync(session.UserId, cancellationToken);
        if (user is not { IsActive: true }
            || await versions.CurrentAsync(user.SubjectId, cancellationToken) > session.SessionVersion)
        {
            return await RefuseAsync(session, endSession: true, cancellationToken);
        }

        return await issuer.RotateAsync(user, session, cancellationToken);
    }

    private async Task<Outcome<SignedInSession>> RefuseAsync(UserSession session, bool endSession, CancellationToken cancellationToken)
    {
        if (endSession)
        {
            await issuer.EndAsync(session, cancellationToken);
        }

        return Rejected();
    }

    private static Refusal Rejected() => Refusal.Invalid(IdentityErrorCodes.RefreshRejected);
}

/// <summary>
/// <c>POST /api/auth/logout</c>: ends the caller's session. Its refresh token stops working and its session id is
/// revoked, so the access token in hand is refused (401) on the next request.
/// </summary>
public sealed class LogoutUseCase(ICurrentUserContext currentUser, IUserSessionRepository sessions, SessionIssuer issuer)
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

        return Done.Value;
    }
}

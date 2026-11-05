using Starter.Application.Results;
using Starter.Modules.Identity.Application.Auditing;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application;

/// <summary>
/// Starts a session for an identity the organization's directory or identity provider has already proven (Active
/// Directory bind, OIDC code flow): matches the account on one field, refuses an unknown or deactivated account and an
/// account bound to another mode with the same <see cref="IdentityErrorCodes.InvalidCredentials"/>, records the auth
/// event and starts the session through the one <see cref="SessionIssuer"/>. Identity's lockout counter does not apply:
/// the directory or provider owns the credential and its lockout.
/// </summary>
public sealed class SignInExternalAccount(IUserAccountRepository accounts, SessionIssuer issuer, IAuthEventSink audit)
{
    /// <summary>The signed-in session, or <see cref="IdentityErrorCodes.InvalidCredentials"/>.</summary>
    /// <param name="field">The account field the external identity is matched on.</param>
    /// <param name="value">The external identity's value for <paramref name="field"/> (compared normalized).</param>
    /// <param name="mode"><see cref="SignInMode.ActiveDirectory"/> or <see cref="SignInMode.Sso"/>.</param>
    /// <param name="idTokenHint">For SSO, the provider's id_token kept for its end-session call; otherwise null.</param>
    /// <param name="cancellationToken">Cancels the sign-in.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is basic (basic login has its own use case).</exception>
    public async Task<Outcome<LoginResult>> ExecuteAsync(
        AccountMatchField field, string value, SignInMode mode, string? idTokenHint, CancellationToken cancellationToken)
    {
        if (mode is not (SignInMode.ActiveDirectory or SignInMode.Sso))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "External sign-in is for Active Directory and SSO only.");
        }

        var account = string.IsNullOrWhiteSpace(value) ? null : await accounts.FindByMatchAsync(field, value, cancellationToken);
        if (ReasonToRefuse(account, mode) is { } reason)
        {
            await audit.RecordAsync(
                new AuthEventRecord(AuthEventType.LoginFailed, UserId: account?.Id, UserName: value, Mode: mode, Reason: reason),
                cancellationToken);
            return Refusal.Invalid(IdentityErrorCodes.InvalidCredentials);
        }

        var session = await issuer.StartAsync(account!, new SessionStart(mode, idTokenHint), cancellationToken);
        await audit.RecordAsync(
            new AuthEventRecord(AuthEventType.LoginSucceeded, UserId: account!.Id, UserName: value, Mode: mode),
            cancellationToken);
        return session;
    }

    private static AuthFailureReason? ReasonToRefuse(UserAccount? account, SignInMode mode) => account switch
    {
        null => AuthFailureReason.UnknownAccount,
        { IsActive: false } => AuthFailureReason.AccountInactive,
        _ when !AccountBindings.Accepts(account.Binding, mode) => AuthFailureReason.BindingMismatch,
        _ => null,
    };
}

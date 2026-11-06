using Codelab.Security.PublicIds;
using Starter.Application.Results;
using Starter.Application.Users;
using Starter.Domain.Users;

namespace Starter.Modules.Users.Application.Accounts;

/// <summary>
/// <c>POST /api/admin/users/{id}/deactivate|reactivate</c>. Deactivation ends access at once: Identity bumps the
/// user's session version, so the next API call of every open session answers 401. The caller must reach the account
/// (<see cref="AccountAccess.FindForChangeAsync"/>: scope, break-glass, privilege), cannot deactivate their own
/// account, and cannot deactivate the last access manager.
/// </summary>
public sealed class SetUserActive(IAccountAdministration accounts, AccountAccess access)
{
    /// <summary>
    /// Done, or a refusal: the refusals of <see cref="AccountAccess.FindForChangeAsync"/>,
    /// <see cref="UsersErrorCodes.CannotDeactivateSelf"/>, <see cref="UsersErrorCodes.LastAccessManager"/> (409), or
    /// Identity's refusal.
    /// </summary>
    public async Task<Outcome<Done>> ExecuteAsync(PublicId<UsersIds> id, bool isActive, CancellationToken cancellationToken)
    {
        var found = await access.FindForChangeAsync(id.Value, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Refusal;
        }

        var account = found.Value;
        if (!isActive && access.IsCaller(account))
        {
            return Refusal.Conflict(UsersErrorCodes.CannotDeactivateSelf);
        }

        if (!isActive && account.IsActive && await access.IsLastAccessManagerAsync(account, cancellationToken))
        {
            return Refusal.Conflict(UsersErrorCodes.LastAccessManager);
        }

        return await accounts.SetActiveAsync(account.UserId, isActive, cancellationToken);
    }
}

/// <summary>
/// <c>POST /api/admin/users/{id}/unlock</c>: ends a lockout and resets the failed sign-in counter. Organization scope
/// applies; the privilege rule does not (an unlock hands the caller nothing), so the break-glass account can be
/// unlocked too.
/// </summary>
public sealed class UnlockUser(IAccountAdministration accounts, AccountAccess access)
{
    /// <summary>Done, or <see cref="UsersErrorCodes.UserNotFound"/> (also outside the caller's scope), or Identity's refusal.</summary>
    public async Task<Outcome<Done>> ExecuteAsync(PublicId<UsersIds> id, CancellationToken cancellationToken)
    {
        if (await access.FindAsync(id.Value, UsersPermissionKeys.Manage, cancellationToken) is not { } account)
        {
            return Refusal.NotFound(UsersErrorCodes.UserNotFound);
        }

        return await accounts.UnlockAsync(account.UserId, cancellationToken);
    }
}

/// <summary>
/// <c>POST /api/admin/users/{id}/activation</c>: issues a new activation link for a password-bound account (the
/// administrator's password reset). Identity clears the current password and bumps the session version, so the user
/// is signed out everywhere until they set a new password through the link. The link is e-mailed when the account has
/// an address (<see cref="AccountActivations"/>), otherwise returned. The link hands its holder the account, so
/// the caller must reach it (<see cref="AccountAccess.FindForChangeAsync"/>: scope, break-glass, privilege).
/// </summary>
public sealed class IssueUserActivation(AccountAccess access, AccountActivations activations)
{
    /// <summary>
    /// The link and expiry, or a refusal: the refusals of <see cref="AccountAccess.FindForChangeAsync"/>,
    /// <see cref="UsersErrorCodes.ActivationNotApplicable"/>, <see cref="UsersErrorCodes.ActivationLinkUnconfigured"/>,
    /// or Identity's refusal.
    /// </summary>
    public async Task<Outcome<IssuedActivation>> ExecuteAsync(PublicId<UsersIds> id, CancellationToken cancellationToken)
    {
        var found = await access.FindForChangeAsync(id.Value, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Refusal;
        }

        return found.Value.Binding == AccountBinding.Password
            ? await activations.IssueAsync(found.Value, cancellationToken)
            : Refusal.Unprocessable(UsersErrorCodes.ActivationNotApplicable);
    }
}

using Starter.Application.Authorization;
using Starter.Application.Results;
using Starter.Application.Users;

namespace Starter.Modules.Users.Application.Accounts;

/// <summary>
/// The one owner of "which accounts may this caller reach, and where may they put one" for the user admin use cases:
/// organization scope for reads and writes (an account outside the caller's scope is not found), for a change the
/// break-glass rule and the privilege rule (the caller must hold everything the account holds, see
/// <see cref="IAdministrationGuard"/>), and the placement rule (scope plus <see cref="OrganizationMembership"/>).
/// </summary>
public sealed class AccountAccess(
    IAccountAdministration accounts,
    IOrganizationScope scope,
    IAdministrationGuard guard,
    ICurrentUserContext currentUser,
    IOrganizationRepository organizations)
{
    /// <summary>The account when it exists and <paramref name="permissionKey"/> reaches its organization; otherwise null.</summary>
    public async Task<AccountSummary?> FindAsync(long userId, string permissionKey, CancellationToken cancellationToken)
    {
        if (await accounts.FindAsync(userId, cancellationToken) is not { } account)
        {
            return null;
        }

        return (await scope.ScopeForAsync(permissionKey, cancellationToken)).Reaches(account.OrganizationId) ? account : null;
    }

    /// <summary>
    /// The account a <see cref="UsersPermissionKeys.Manage"/> holder may change, or a refusal:
    /// <see cref="UsersErrorCodes.UserNotFound"/> (404, also outside the caller's scope),
    /// <see cref="UsersErrorCodes.BreakGlassProtected"/> (409) or <see cref="UsersErrorCodes.TargetOutranksCaller"/> (403).
    /// </summary>
    public async Task<Outcome<AccountSummary>> FindForChangeAsync(long userId, CancellationToken cancellationToken)
    {
        if (await FindAsync(userId, UsersPermissionKeys.Manage, cancellationToken) is not { } account)
        {
            return Refusal.NotFound(UsersErrorCodes.UserNotFound);
        }

        if (account.IsBreakGlass)
        {
            return Refusal.Conflict(UsersErrorCodes.BreakGlassProtected);
        }

        return await guard.CallerCoversAsync(account.SubjectId, cancellationToken)
            ? account
            : Refusal.Forbidden(UsersErrorCodes.TargetOutranksCaller);
    }

    /// <summary>True when <paramref name="account"/> is the caller's own account.</summary>
    public bool IsCaller(AccountSummary account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return currentUser.SubjectId == account.SubjectId;
    }

    /// <summary>
    /// Why <paramref name="changes"/> cannot place the account (<paramref name="current"/>: null for a new account), or
    /// null: <see cref="UsersErrorCodes.OrganizationOutOfScope"/> (403) when a new account or a move lands outside the
    /// caller's <see cref="UsersPermissionKeys.Manage"/> scope, then the membership codes. Keeping the current
    /// organization is allowed after it was deactivated.
    /// </summary>
    public async Task<Refusal?> PlacementRefusalAsync(AccountChanges changes, AccountSummary? current, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var moves = current is null || changes.OrganizationId != current.OrganizationId;
        if (moves && !(await scope.ScopeForAsync(UsersPermissionKeys.Manage, cancellationToken)).Reaches(changes.OrganizationId))
        {
            return Refusal.Forbidden(UsersErrorCodes.OrganizationOutOfScope);
        }

        var organization = changes.OrganizationId is { } organizationId ? await organizations.FindAsync(organizationId, cancellationToken) : null;
        return OrganizationMembership.RefusalOf(OrganizationMembership.Check(
            changes.Kind,
            organizationRequested: changes.OrganizationId is not null,
            organization,
            alreadyMember: !moves && changes.OrganizationId is not null));
    }

    /// <summary>The organization filter of a list under <paramref name="permissionKey"/> that asked for <paramref name="requestedOrganizationId"/>.</summary>
    public async Task<OrganizationFilter> ListFilterAsync(string permissionKey, long? requestedOrganizationId, CancellationToken cancellationToken) =>
        (await scope.ScopeForAsync(permissionKey, cancellationToken)).Narrow(requestedOrganizationId);

    /// <summary>True when switching <paramref name="account"/> off would leave no access manager other than break-glass.</summary>
    public Task<bool> IsLastAccessManagerAsync(AccountSummary account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        return guard.IsLastAccessManagerAsync(account.UserId, cancellationToken);
    }
}

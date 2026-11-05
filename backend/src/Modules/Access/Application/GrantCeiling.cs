using Starter.Application.Authorization;
using Starter.Application.Results;
using Starter.Application.Users;

namespace Starter.Modules.Access.Application;

/// <summary>
/// How far a grant write may raise an account's access: no one hands out more than they hold. Only the entries the
/// write adds or widens are judged (an entry the account already holds at that scope stays, whoever set it):
/// <list type="bullet">
///   <item>the caller never raises their own grants;</item>
///   <item>a role is assigned at most as widely as the caller holds <see cref="AccessPermissionKeys.GrantsManage"/>;</item>
///   <item>a direct grant needs the caller to cover the key at that scope (<see cref="PermissionCoverage"/>).</item>
/// </list>
/// A caller without a subject, or one who no longer holds <see cref="AccessPermissionKeys.GrantsManage"/>, raises nothing.
/// </summary>
public sealed class GrantCeiling(ICurrentUserContext currentUser, IUserPermissionResolver resolver, IUserGrantReader reader)
{
    /// <summary>True when the signed-in caller may apply <paramref name="request"/> to <paramref name="target"/>.</summary>
    public async Task<bool> PermitsAsync(AccountSummary target, GrantRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.SubjectId is not { } callerSubjectId)
        {
            return false;
        }

        var current = await reader.ReadAsync(target.UserId, cancellationToken);
        var raisedRoles = request.Roles.Where(entry => Raises(entry.Scope, HeldScope(current, entry.Role.Id))).Select(entry => entry.Scope).ToList();
        var raisedKeys = request.Permissions.Where(entry => Raises(entry.Scope, HeldScope(current, entry.Permission.Key))).ToList();
        if (raisedRoles.Count == 0 && raisedKeys.Count == 0)
        {
            return true;
        }

        if (callerSubjectId == target.SubjectId)
        {
            return false;
        }

        var caller = await resolver.ResolveAccessAsync(callerSubjectId, cancellationToken);
        if (caller.Find(AccessPermissionKeys.GrantsManage) is not { } manage)
        {
            return false;
        }

        return raisedRoles.TrueForAll(scope => !Raises(scope, manage.Scope))
            && raisedKeys.TrueForAll(entry => PermissionCoverage.Covers(caller, entry.Permission.Key, entry.Scope, target.OrganizationId));
    }

    /// <summary>True when <paramref name="requested"/> reaches further than <paramref name="held"/> (null: not held).</summary>
    private static bool Raises(PermissionScope requested, PermissionScope? held) =>
        held is not { } scope || PermissionResolution.Widest(requested, scope) != scope;

    private static PermissionScope? HeldScope(UserGrantSnapshot current, long roleId) =>
        current.Assigned.FirstOrDefault(assigned => assigned.Role.RoleId == roleId)?.Scope;

    private static PermissionScope? HeldScope(UserGrantSnapshot current, string key) =>
        current.Direct.FirstOrDefault(grant => string.Equals(grant.Key, key, StringComparison.Ordinal))?.Scope;
}

/// <summary>
/// The checks a user grant write passes, in order: the break-glass account is not edited (409
/// <see cref="AccessErrorCodes.BreakGlassNotEditable"/>), the caller grants no more than they hold (403
/// <see cref="AccessErrorCodes.ScopeExceedsCaller"/>, <see cref="GrantCeiling"/>) and the write keeps a manager (409
/// <see cref="AccessErrorCodes.LastManager"/>, <see cref="ManagerContinuityGuard"/>).
/// </summary>
public sealed class UserGrantWriteGuards(GrantCeiling ceiling, ManagerContinuityGuard continuity)
{
    /// <summary>The continuity verdict to record with the write, or the first refusal.</summary>
    public async Task<Outcome<GuardVerdict>> CheckAsync(
        AccountSummary account,
        GrantRequest request,
        bool confirmBreakGlassOnly,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(request);
        if (account.IsBreakGlass)
        {
            return Refusal.Conflict(AccessErrorCodes.BreakGlassNotEditable);
        }

        if (!await ceiling.PermitsAsync(account, request, cancellationToken))
        {
            return Refusal.Forbidden(AccessErrorCodes.ScopeExceedsCaller);
        }

        var change = new UserGrantChange(
            UserId: account.UserId,
            RoleIds: [.. request.Roles.Select(entry => entry.Role.Id)],
            Keys: [.. request.Permissions.Select(entry => entry.Permission.Key)]);
        var verdict = await continuity.CheckAsync(change, confirmBreakGlassOnly, cancellationToken);
        return verdict is GuardVerdict.Refused ? Refusal.Conflict(AccessErrorCodes.LastManager) : verdict;
    }
}

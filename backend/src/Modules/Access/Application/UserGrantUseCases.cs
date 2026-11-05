using Starter.Application.Authorization;
using Starter.Application.Results;
using Starter.Application.Users;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Application;

/// <summary>Everything the grant editor shows for one user.</summary>
/// <param name="Account">The account (from the kernel account port).</param>
/// <param name="Roles">The ordinary roles assigned to the user, with their scopes, by role id.</param>
/// <param name="Permissions">The direct grants, by key.</param>
/// <param name="VirtualRoles">The virtual roles that match the user's kind (active or not), by role id.</param>
/// <param name="Effective">What the user holds now (the live resolve), by key.</param>
/// <param name="Warnings">Warning codes raised by the write that produced this view; empty on a read.</param>
public sealed record UserGrantsView(
    AccountSummary Account,
    IReadOnlyList<AssignedRole> Roles,
    IReadOnlyList<DirectGrant> Permissions,
    IReadOnlyList<RoleGrants> VirtualRoles,
    IReadOnlyList<EffectiveGrant> Effective,
    IReadOnlyList<string> Warnings);

/// <summary>Builds a <see cref="UserGrantsView"/> from the current rows and the resolve rule (one owner of the view).</summary>
public sealed class UserGrantsViewBuilder(IUserGrantReader reader, PermissionCatalog catalog)
{
    /// <summary>The view of <paramref name="account"/>, carrying <paramref name="warnings"/>.</summary>
    public async Task<UserGrantsView> BuildAsync(AccountSummary account, IReadOnlyList<string> warnings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        var snapshot = await reader.ReadAsync(account.UserId, cancellationToken);
        var facts = new AccountFacts(IsActive: account.IsActive, IsBreakGlass: account.IsBreakGlass, Kind: account.Kind);
        return new UserGrantsView(
            Account: account,
            Roles: [.. snapshot.Assigned.OrderBy(assigned => assigned.Role.RoleId)],
            Permissions: [.. snapshot.Direct.OrderBy(grant => grant.Key, StringComparer.Ordinal)],
            VirtualRoles: [.. snapshot.VirtualRoles.Where(role => role.VirtualKind is { } kind && kind.Reaches(account.Kind)).OrderBy(role => role.RoleId)],
            Effective: PermissionResolution.Resolve(facts, snapshot, catalog),
            Warnings: warnings);
    }
}

/// <summary>
/// Replaces one user's role assignments and direct grants with a <see cref="GrantRequest"/> (diff: removes, rescopes,
/// adds) and commits once. Live: the next request of that user resolves the new set, no session bump.
/// </summary>
public sealed class UserGrantWriter(
    IUserRoleAssignmentRepository assignments,
    IUserPermissionGrantRepository grants,
    AccessChanges changes)
{
    /// <summary>Applies <paramref name="request"/> to <paramref name="userId"/> and commits.</summary>
    public async Task ApplyAsync(long userId, GrantRequest request, GuardVerdict verdict, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await ApplyRolesAsync(userId, request.Roles, cancellationToken);
        await ApplyPermissionsAsync(userId, request.Permissions, cancellationToken);
        await changes.CommitAsync(
            () => new AccessAuditEvent(AccessChangeKind.UserGrantsReplaced, userId, RoleCount: request.Roles.Count, PermissionCount: request.Permissions.Count, verdict),
            cancellationToken);
    }

    private async Task ApplyRolesAsync(long userId, IReadOnlyList<(Role Role, PermissionScope Scope)> wanted, CancellationToken cancellationToken)
    {
        var scopes = wanted.ToDictionary(entry => entry.Role.Id, entry => entry.Scope);
        var existing = await assignments.ForUserAsync(userId, cancellationToken);
        foreach (var assignment in existing)
        {
            if (scopes.TryGetValue(assignment.RoleId, out var scope))
            {
                assignment.ChangeScope(scope, changes.Now);
                continue;
            }

            assignments.Remove(assignment);
        }

        var held = existing.Select(assignment => assignment.RoleId).ToHashSet();
        foreach (var (role, scope) in wanted.Where(entry => !held.Contains(entry.Role.Id)))
        {
            assignments.Add(UserRoleAssignment.Create(userId: userId, role: role, scope: scope, assignedAt: changes.Now));
        }
    }

    private async Task ApplyPermissionsAsync(long userId, IReadOnlyList<(PermissionEntry Permission, PermissionScope Scope)> wanted, CancellationToken cancellationToken)
    {
        var scopes = wanted.ToDictionary(entry => entry.Permission.Key, entry => entry.Scope, StringComparer.Ordinal);
        var existing = await grants.ForUserAsync(userId, cancellationToken);
        foreach (var grant in existing)
        {
            if (scopes.TryGetValue(grant.Permission.Key, out var scope))
            {
                grant.ChangeScope(scope, changes.Now);
                continue;
            }

            grants.Remove(grant);
        }

        var held = existing.Select(grant => grant.Permission.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var (permission, scope) in wanted.Where(entry => !held.Contains(entry.Permission.Key)))
        {
            grants.Add(UserPermissionGrant.Create(userId: userId, permission: permission, grantedAt: changes.Now, scope: scope));
        }
    }
}

/// <summary>
/// The accounts the grant editor reaches: an account outside the caller's organization scope for the permission is not
/// found (one owner of the scope rule for the editor).
/// </summary>
public sealed class ScopedAccounts(IAccountAdministration accounts, IOrganizationScope scope)
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
}

/// <summary>
/// Reads one user's grants for the editor. The permission is enforced by the endpoint; a holder of
/// <see cref="AccessPermissionKeys.GrantsView"/> at own-organization scope reaches only their organization's accounts.
/// </summary>
public sealed class GetUserGrants(ScopedAccounts accounts, UserGrantsViewBuilder views)
{
    /// <summary>The view, or a 404 refusal (<see cref="AccessErrorCodes.UserNotFound"/>, also outside the caller's scope).</summary>
    public async Task<Outcome<UserGrantsView>> ExecuteAsync(long userId, CancellationToken cancellationToken)
    {
        if (await accounts.FindAsync(userId, AccessPermissionKeys.GrantsView, cancellationToken) is not { } account)
        {
            return Refusal.NotFound(AccessErrorCodes.UserNotFound);
        }

        return await views.BuildAsync(account, warnings: [], cancellationToken);
    }
}

/// <summary>
/// Replaces one user's roles and direct grants, behind <see cref="UserGrantWriteGuards"/>. The permission is
/// enforced by the endpoint; a holder of <see cref="AccessPermissionKeys.GrantsManage"/> at own-organization scope
/// reaches only their organization's accounts, and no caller grants more than they hold (<see cref="GrantCeiling"/>).
/// </summary>
public sealed class PutUserGrants(
    ScopedAccounts accounts,
    GrantRequestReader requests,
    UserGrantWriteGuards guards,
    UserGrantWriter writer,
    UserGrantsViewBuilder views)
{
    /// <summary>
    /// The new view; 400 for invalid input, an unknown role, a virtual role or an unknown/retired key; 404 for an
    /// unknown user or one outside the caller's scope; 403 (<see cref="AccessErrorCodes.ScopeExceedsCaller"/>) when the
    /// write raises the caller's own grants or grants more than the caller holds; 409 for the break-glass account or for
    /// removing the last manager.
    /// </summary>
    public async Task<Outcome<UserGrantsView>> ExecuteAsync(long userId, PutUserGrantsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = await requests.ReadAsync(command, cancellationToken);
        if (!request.IsSuccess)
        {
            return request.Refusal;
        }

        if (await accounts.FindAsync(userId, AccessPermissionKeys.GrantsManage, cancellationToken) is not { } account)
        {
            return Refusal.NotFound(AccessErrorCodes.UserNotFound);
        }

        var verdict = await guards.CheckAsync(account, request.Value, command.ConfirmBreakGlassOnly, cancellationToken);
        if (!verdict.IsSuccess)
        {
            return verdict.Refusal;
        }

        await writer.ApplyAsync(userId, request.Value, verdict.Value, cancellationToken);
        return await views.BuildAsync(account, GuardWarnings.Of(verdict.Value), cancellationToken);
    }
}

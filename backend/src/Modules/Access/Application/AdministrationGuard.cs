using Starter.Application.Authorization;
using Starter.Application.Users;

namespace Starter.Modules.Access.Application;

/// <summary>
/// The kernel <see cref="IAdministrationGuard"/>: compares the caller's live access with what the target account would
/// hold while active (so a deactivated administrator is not an easy target), and asks the self-lockout guard whether
/// switching a user off removes the last manager.
/// </summary>
public sealed class AdministrationGuard(
    ICurrentUserContext currentUser,
    IUserPermissionResolver resolver,
    ManagerContinuityGuard continuity) : IAdministrationGuard
{
    /// <inheritdoc />
    public async Task<bool> CallerCoversAsync(Guid targetSubjectId, CancellationToken cancellationToken)
    {
        if (currentUser.SubjectId is not { } callerSubjectId)
        {
            return false;
        }

        var target = await resolver.ResolvePotentialAccessAsync(targetSubjectId, cancellationToken);
        if (target.User is null)
        {
            return false;
        }

        var caller = await resolver.ResolveAccessAsync(callerSubjectId, cancellationToken);
        return target.Permissions.All(held => PermissionCoverage.Covers(caller, held.Key, held.Scope, target.User.OrganizationId));
    }

    /// <inheritdoc />
    public async Task<bool> IsLastAccessManagerAsync(long userId, CancellationToken cancellationToken) =>
        await continuity.CheckDeactivationAsync(userId, cancellationToken) is GuardVerdict.Refused;
}

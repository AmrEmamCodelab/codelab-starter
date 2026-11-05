using Starter.Application.Authorization;
using Starter.Application.Users;

namespace Starter.Modules.Access.Application;

/// <summary>
/// The kernel <see cref="IOrganizationScope"/> for the signed-in caller, over the live resolver: a module's query
/// asks how far a permission reaches and narrows to <see cref="ScopeDecision.OrganizationId"/> when the answer is
/// <see cref="PermissionScope.OwnOrganization"/>. A caller without a subject, an unknown or inactive user and a key the
/// user does not hold all get <see cref="ScopeDecision.Denied"/>. An own-organization holder without an organization
/// gets an allowed decision with a null organization id: such a query must match nothing.
/// </summary>
public sealed class CurrentUserOrganizationScope(ICurrentUserContext currentUser, IUserPermissionResolver resolver) : IOrganizationScope
{
    /// <inheritdoc />
    public async Task<ScopeDecision> ScopeForAsync(string permissionKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionKey);
        if (currentUser.SubjectId is not { } subjectId)
        {
            return ScopeDecision.Denied;
        }

        var access = await resolver.ResolveAccessAsync(subjectId, cancellationToken);
        if (access.Find(permissionKey) is not { } held)
        {
            return ScopeDecision.Denied;
        }

        return held.Scope is PermissionScope.Global
            ? new ScopeDecision(Allowed: true, Scope: PermissionScope.Global, OrganizationId: null)
            : new ScopeDecision(Allowed: true, Scope: PermissionScope.OwnOrganization, OrganizationId: access.User?.OrganizationId);
    }
}

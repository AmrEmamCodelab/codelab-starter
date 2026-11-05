using Starter.Application.Authorization;

namespace Starter.Modules.Access.Application;

/// <summary>
/// The one rule for "the caller's own access covers a permission held at a scope by a target account": the
/// administration guard uses it to compare a caller with an account, and the grant ceiling uses it before the caller
/// hands out a direct grant.
/// </summary>
public static class PermissionCoverage
{
    /// <summary>
    /// True when <paramref name="caller"/> holds <paramref name="key"/> globally, or holds it at own-organization scope,
    /// <paramref name="scope"/> is <see cref="PermissionScope.OwnOrganization"/> and the target belongs to the caller's
    /// organization (<paramref name="targetOrganizationId"/>; null never matches).
    /// </summary>
    public static bool Covers(ResolvedAccess caller, string key, PermissionScope scope, long? targetOrganizationId)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (caller.Find(key) is not { } own)
        {
            return false;
        }

        if (own.Scope is PermissionScope.Global)
        {
            return true;
        }

        return scope is PermissionScope.OwnOrganization
            && caller.User?.OrganizationId is { } callerOrganizationId
            && callerOrganizationId == targetOrganizationId;
    }
}

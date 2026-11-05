using Starter.Application.Users;

namespace Starter.Modules.Access.Application;

/// <summary>
/// Resolves a user's effective permissions live, on every call, from the current rows: the token carries identity
/// only, so a grant, a revocation or a deactivation bites on the very next request without a new sign-in.
/// </summary>
public interface IUserPermissionResolver
{
    /// <summary>
    /// The catalog keys the user with token subject <paramref name="subjectId"/> holds now: none for an unknown or
    /// inactive user, the whole catalog for a user who holds all permissions (break-glass), otherwise the user's
    /// grants that are still in the catalog.
    /// </summary>
    Task<IReadOnlySet<string>> ResolveAsync(Guid subjectId, CancellationToken cancellationToken);
}

/// <summary>
/// The live <see cref="IUserPermissionResolver"/>: the account state comes through the kernel
/// <see cref="IUserDirectory"/> port (never from the Identity module), the grants from this module's store. There is
/// no cache, so nothing can serve a stale grant; one resolve is two indexed lookups.
/// </summary>
public sealed class UserPermissionResolver(
    IUserDirectory directory,
    IUserPermissionGrantRepository grants,
    PermissionCatalog catalog) : IUserPermissionResolver
{
    private static readonly IReadOnlySet<string> NoPermissions = new HashSet<string>(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> ResolveAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        var user = await directory.FindBySubjectAsync(subjectId, cancellationToken);
        if (user is not { IsActive: true })
        {
            return NoPermissions;
        }

        if (user.HoldsAllPermissions)
        {
            return catalog.Keys;
        }

        var granted = await grants.GrantedKeysAsync(user.UserId, cancellationToken);
        return new HashSet<string>(granted.Where(catalog.Contains), StringComparer.Ordinal);
    }
}

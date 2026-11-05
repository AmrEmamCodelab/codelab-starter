namespace Starter.Application.Authorization;

/// <summary>
/// Grants permissions to a user directly (implemented by the Access module). Used by account seeding; the role and
/// grant editor arrives in phase 3.
/// </summary>
public interface IUserPermissionGrants
{
    /// <summary>
    /// Ensures <paramref name="userId"/> holds every key in <paramref name="permissionKeys"/> (idempotent). A key that
    /// is not in the catalog is a configuration fault and throws <see cref="InvalidOperationException"/>.
    /// </summary>
    Task EnsureGrantedAsync(long userId, IReadOnlyCollection<string> permissionKeys, CancellationToken cancellationToken);
}

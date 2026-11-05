using Starter.Application.Users;

namespace Starter.Modules.Access.Application;

/// <summary>A user's live access: the directory entry (null when unknown) and the effective permissions.</summary>
/// <param name="User">The user, or null when no account has the subject.</param>
/// <param name="Permissions">The effective permissions, sorted by key; empty for an unknown or inactive user.</param>
public sealed record ResolvedAccess(DirectoryUser? User, IReadOnlyList<EffectiveGrant> Permissions)
{
    /// <summary>The effective permission with <paramref name="key"/>, or null when the user does not hold it.</summary>
    public EffectiveGrant? Find(string key) =>
        Permissions.FirstOrDefault(permission => string.Equals(permission.Key, key, StringComparison.Ordinal));
}

/// <summary>
/// Resolves a user's effective permissions live, on every call, from the current rows: the token carries identity
/// only, so a grant, a revocation, a role change or a deactivation bites on the very next request without a new
/// sign-in (and without a session-version bump).
/// </summary>
public interface IUserPermissionResolver
{
    /// <summary>The catalog keys the user with token subject <paramref name="subjectId"/> holds now.</summary>
    Task<IReadOnlySet<string>> ResolveAsync(Guid subjectId, CancellationToken cancellationToken);

    /// <summary>The user and their effective permissions with scopes and sources.</summary>
    Task<ResolvedAccess> ResolveAccessAsync(Guid subjectId, CancellationToken cancellationToken);

    /// <summary>
    /// The user and the permissions they would hold while active: a deactivated account is resolved as if it were
    /// reactivated (for privilege comparisons, so switching an account off does not lower its rank).
    /// </summary>
    Task<ResolvedAccess> ResolvePotentialAccessAsync(Guid subjectId, CancellationToken cancellationToken);
}

/// <summary>
/// The live <see cref="IUserPermissionResolver"/>: the account state comes through the kernel
/// <see cref="IUserDirectory"/> port (never from the Identity module), the grants from this module's store, and
/// <see cref="PermissionResolution"/> applies the rule. There is no cache, so nothing can serve a stale grant.
/// </summary>
public sealed class UserPermissionResolver(
    IUserDirectory directory,
    IUserGrantReader grants,
    PermissionCatalog catalog) : IUserPermissionResolver
{
    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> ResolveAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(subjectId, cancellationToken);
        return new HashSet<string>(access.Permissions.Select(permission => permission.Key), StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public Task<ResolvedAccess> ResolveAccessAsync(Guid subjectId, CancellationToken cancellationToken) =>
        ResolveAsync(subjectId, asIfActive: false, cancellationToken);

    /// <inheritdoc />
    public Task<ResolvedAccess> ResolvePotentialAccessAsync(Guid subjectId, CancellationToken cancellationToken) =>
        ResolveAsync(subjectId, asIfActive: true, cancellationToken);

    private async Task<ResolvedAccess> ResolveAsync(Guid subjectId, bool asIfActive, CancellationToken cancellationToken)
    {
        var user = await directory.FindBySubjectAsync(subjectId, cancellationToken);
        if (user is null)
        {
            return new ResolvedAccess(User: null, Permissions: []);
        }

        var facts = new AccountFacts(IsActive: asIfActive || user.IsActive, IsBreakGlass: user.HoldsAllPermissions, Kind: user.Kind);
        var snapshot = PermissionResolution.NeedsGrants(facts) ? await grants.ReadAsync(user.UserId, cancellationToken) : null;
        return new ResolvedAccess(user, PermissionResolution.Resolve(facts, snapshot, catalog));
    }
}

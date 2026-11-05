using Starter.Domain.Users;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Application;

/// <summary>A role as the manager graph sees it: its state and the manager keys it grants.</summary>
/// <param name="RoleId">The internal role id.</param>
/// <param name="IsActive">False when the role grants nothing.</param>
/// <param name="VirtualKind">The users a virtual role reaches, or null for an assignable role.</param>
/// <param name="Keys">The manager keys (<see cref="ManagerGraph.ManagerKeys"/>) the role grants.</param>
public sealed record ManagerRole(long RoleId, bool IsActive, VirtualRoleKind? VirtualKind, IReadOnlySet<string> Keys);

/// <summary>A user who holds at least one manager key through this module's rows.</summary>
/// <param name="UserId">The internal user id.</param>
/// <param name="RoleIds">The ordinary roles assigned to the user (only roles that matter to the graph need be listed).</param>
/// <param name="DirectKeys">The manager keys granted to the user directly.</param>
public sealed record ManagerCandidate(long UserId, IReadOnlySet<long> RoleIds, IReadOnlySet<string> DirectKeys);

/// <summary>
/// The part of the grant graph that decides who can administer access: a "manager" is an active user, other than
/// break-glass, who holds BOTH <see cref="AccessPermissionKeys.RolesManage"/> and
/// <see cref="AccessPermissionKeys.GrantsManage"/> at any scope, through direct grants, active assigned roles and
/// active virtual roles matching their kind. Immutable: a pending change is simulated with <see cref="WithRole"/> or
/// <see cref="WithCandidate"/>, so the guard compares the world before and after without writing anything.
/// </summary>
public sealed class ManagerGraph
{
    private const string FootprintSeparator = "|";

    private readonly Dictionary<long, ManagerRole> _roles;
    private readonly Dictionary<long, ManagerCandidate> _candidates;

    /// <summary>A graph of <paramref name="roles"/> and <paramref name="candidates"/>.</summary>
    public ManagerGraph(IEnumerable<ManagerRole> roles, IEnumerable<ManagerCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(candidates);
        _roles = roles.ToDictionary(role => role.RoleId);
        _candidates = candidates.ToDictionary(candidate => candidate.UserId);
    }

    /// <summary>The keys a manager must hold, both of them.</summary>
    public static IReadOnlySet<string> ManagerKeys { get; } =
        new HashSet<string>([AccessPermissionKeys.RolesManage, AccessPermissionKeys.GrantsManage], StringComparer.Ordinal);

    /// <summary>The candidates, by user id.</summary>
    public IEnumerable<ManagerCandidate> Candidates => _candidates.Values.OrderBy(candidate => candidate.UserId);

    /// <summary>True when <paramref name="keys"/> holds every manager key.</summary>
    public static bool IsComplete(IReadOnlySet<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return ManagerKeys.All(keys.Contains);
    }

    /// <summary>The manager keys a user of <paramref name="kind"/> gets from the active virtual roles alone.</summary>
    public IReadOnlySet<string> VirtualKeys(UserKind kind) => Union(
        _roles.Values.Where(role => role is { IsActive: true, VirtualKind: { } virtualKind } && virtualKind.Reaches(kind)).SelectMany(role => role.Keys));

    /// <summary>The manager keys <paramref name="candidate"/> holds through direct grants and active assigned roles.</summary>
    public IReadOnlySet<string> RowKeys(ManagerCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var fromRoles = candidate.RoleIds
            .Select(roleId => _roles.GetValueOrDefault(roleId))
            .Where(role => role is { IsActive: true, VirtualKind: null })
            .SelectMany(role => role!.Keys);
        return Union(candidate.DirectKeys.Concat(fromRoles));
    }

    /// <summary>
    /// What <paramref name="userId"/> holds through this module's rows (<see cref="RowKeys"/>), as a comparable
    /// string: equal before and after a change means the change cannot alter who is a manager.
    /// </summary>
    public string Footprint(long userId)
    {
        if (!_candidates.TryGetValue(userId, out var candidate))
        {
            return string.Empty;
        }

        return string.Join(FootprintSeparator, RowKeys(candidate).Order(StringComparer.Ordinal));
    }

    /// <summary>The footprint of role <paramref name="roleId"/>: its active state and manager keys.</summary>
    public string RoleFootprint(long roleId) =>
        _roles.TryGetValue(roleId, out var role) && role.Keys.Count > 0
            ? role.IsActive + FootprintSeparator + string.Join(FootprintSeparator, role.Keys.Order(StringComparer.Ordinal))
            : string.Empty;

    /// <summary>A copy in which role <paramref name="roleId"/> has <paramref name="isActive"/> and grants <paramref name="keys"/>.</summary>
    public ManagerGraph WithRole(long roleId, bool isActive, IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var kind = _roles.TryGetValue(roleId, out var existing) ? existing.VirtualKind : null;
        var changed = new ManagerRole(roleId, isActive, kind, ManagerSubset(keys));
        return new ManagerGraph(_roles.Values.Where(role => role.RoleId != roleId).Append(changed), _candidates.Values);
    }

    /// <summary>A copy in which <paramref name="candidate"/> replaces that user's entry (or is added).</summary>
    public ManagerGraph WithCandidate(ManagerCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var trimmed = candidate with { DirectKeys = ManagerSubset(candidate.DirectKeys) };
        return new ManagerGraph(_roles.Values, _candidates.Values.Where(entry => entry.UserId != candidate.UserId).Append(trimmed));
    }

    /// <summary>The subset of <paramref name="keys"/> that are manager keys.</summary>
    public static IReadOnlySet<string> ManagerSubset(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return Union(keys.Where(ManagerKeys.Contains));
    }

    private static HashSet<string> Union(IEnumerable<string> keys) => new(keys, StringComparer.Ordinal);
}

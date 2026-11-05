using Starter.Application.Authorization;
using Starter.Domain.Users;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Application;

/// <summary>What the resolver needs to know about an account (from the kernel user ports).</summary>
/// <param name="IsActive">False for a deactivated account: it holds nothing.</param>
/// <param name="IsBreakGlass">True for the break-glass administrator: it holds the whole catalog, globally.</param>
/// <param name="Kind">Internal or external: selects the virtual roles that reach the user.</param>
public sealed record AccountFacts(bool IsActive, bool IsBreakGlass, UserKind Kind);

/// <summary>Where an effective permission comes from. Stored nowhere; sent as the integer value.</summary>
public enum PermissionSourceKind
{
    /// <summary>A direct grant to the user.</summary>
    Direct = 1,

    /// <summary>An assigned; active; ordinary role.</summary>
    Role = 2,

    /// <summary>An active virtual role that matches the user's kind.</summary>
    VirtualRole = 3,

    /// <summary>The break-glass administrator holds every permission.</summary>
    BreakGlass = 4,
}

/// <summary>One reason a user holds a permission.</summary>
/// <param name="Kind">The kind of source.</param>
/// <param name="RoleId">The role (ordinary or virtual), or null for a direct grant and for break-glass.</param>
/// <param name="Scope">The scope this source grants.</param>
public sealed record PermissionSource(PermissionSourceKind Kind, long? RoleId, PermissionScope Scope);

/// <summary>A permission a user holds now, at the widest scope any source grants, with every source.</summary>
/// <param name="Key">The permission key (in the live catalog).</param>
/// <param name="Scope">The widest scope: <see cref="PermissionScope.Global"/> wins over <see cref="PermissionScope.OwnOrganization"/>.</param>
/// <param name="Sources">Every source, direct grants first, then roles by id.</param>
public sealed record EffectiveGrant(string Key, PermissionScope Scope, IReadOnlyList<PermissionSource> Sources);

/// <summary>
/// The one owner of the resolve rule: break-glass holds all | inactive holds none | otherwise the union of the direct
/// grants, the active assigned roles and the active virtual roles matching the user's kind, filtered to the live
/// catalog (retired keys grant nothing), with <see cref="PermissionScope.Global"/> beating
/// <see cref="PermissionScope.OwnOrganization"/>. Pure: the callers read the facts and the snapshot.
/// </summary>
public static class PermissionResolution
{
    /// <summary>
    /// The scope a virtual role grants. Least privilege: a virtual role reaches every user of a kind, so it never
    /// grants beyond the user's own organization; a global grant needs a real role or a direct grant.
    /// </summary>
    public const PermissionScope VirtualRoleScope = PermissionScope.OwnOrganization;

    private static readonly IReadOnlyList<EffectiveGrant> Nothing = [];

    /// <summary>True when the facts call for reading grants (an active account that is not break-glass).</summary>
    public static bool NeedsGrants(AccountFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts is { IsActive: true, IsBreakGlass: false };
    }

    /// <summary>The effective permissions, sorted by key (ordinal).</summary>
    /// <param name="facts">The account state.</param>
    /// <param name="snapshot">The user's grants; ignored (may be null) unless <see cref="NeedsGrants"/>.</param>
    /// <param name="catalog">The live catalog.</param>
    public static IReadOnlyList<EffectiveGrant> Resolve(AccountFacts facts, UserGrantSnapshot? snapshot, PermissionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!facts.IsActive)
        {
            return Nothing;
        }

        if (facts.IsBreakGlass)
        {
            return [.. catalog.Definitions.Select(definition => BreakGlassPermission(definition.Key))];
        }

        ArgumentNullException.ThrowIfNull(snapshot);
        var sources = SourcesOf(snapshot, facts.Kind).Where(pair => catalog.Contains(pair.Key));
        return
        [
            .. sources
                .GroupBy(pair => pair.Key, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => Effective(group.Key, [.. group.Select(pair => pair.Source)])),
        ];
    }

    /// <summary>The widest of two scopes (<see cref="PermissionScope.Global"/> wins).</summary>
    public static PermissionScope Widest(PermissionScope first, PermissionScope second) =>
        first is PermissionScope.Global || second is PermissionScope.Global ? PermissionScope.Global : PermissionScope.OwnOrganization;

    private static EffectiveGrant BreakGlassPermission(string key) =>
        new(Key: key, Scope: PermissionScope.Global, Sources: [new PermissionSource(PermissionSourceKind.BreakGlass, RoleId: null, PermissionScope.Global)]);

    private static EffectiveGrant Effective(string key, List<PermissionSource> sources) =>
        new(Key: key, Scope: sources.Select(source => source.Scope).Aggregate(Widest), Sources: sources);

    private static IEnumerable<(string Key, PermissionSource Source)> SourcesOf(UserGrantSnapshot snapshot, UserKind kind)
    {
        var direct = snapshot.Direct.Select(grant => (grant.Key, new PermissionSource(PermissionSourceKind.Direct, RoleId: null, grant.Scope)));
        var assigned = snapshot.Assigned
            .Where(assignment => assignment.Role is { IsActive: true, VirtualKind: null })
            .OrderBy(assignment => assignment.Role.RoleId)
            .SelectMany(assignment => assignment.Role.Keys.Select(key =>
                (key, new PermissionSource(PermissionSourceKind.Role, assignment.Role.RoleId, assignment.Scope))));
        var reached = snapshot.VirtualRoles
            .Where(role => role is { IsActive: true, VirtualKind: { } virtualKind } && virtualKind.Reaches(kind))
            .OrderBy(role => role.RoleId)
            .SelectMany(role => role.Keys.Select(key =>
                (key, new PermissionSource(PermissionSourceKind.VirtualRole, role.RoleId, VirtualRoleScope))));
        return direct.Concat(assigned).Concat(reached);
    }
}

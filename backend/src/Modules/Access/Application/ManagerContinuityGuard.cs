using Starter.Application.Users;
using Starter.Domain.Users;

namespace Starter.Modules.Access.Application;

/// <summary>What the self-lockout guard decided about a pending change. Never stored.</summary>
public enum GuardVerdict
{
    /// <summary>The change keeps at least one manager, or there was none before it.</summary>
    Allowed = 1,

    /// <summary>The change removes the last manager, confirmed by the caller while break-glass is available.</summary>
    AllowedBreakGlassOnly = 2,

    /// <summary>The change removes the last manager: refused with <see cref="AccessErrorCodes.LastManager"/>.</summary>
    Refused = 3,
}

/// <summary>A pending write to one user's roles and direct grants.</summary>
/// <param name="UserId">The internal user id.</param>
/// <param name="RoleIds">The ordinary roles the user will hold.</param>
/// <param name="Keys">The keys the user will hold directly.</param>
public sealed record UserGrantChange(long UserId, IReadOnlyCollection<long> RoleIds, IReadOnlyCollection<string> Keys);

/// <summary>A pending write to one role.</summary>
/// <param name="RoleId">The internal role id.</param>
/// <param name="IsActive">The role's active state after the write.</param>
/// <param name="Keys">The keys the role will grant.</param>
public sealed record RoleChange(long RoleId, bool IsActive, IReadOnlyCollection<string> Keys);

/// <summary>
/// The self-lockout guard (rewritten from the source's reachability checker for the union model). A write may not take
/// the number of managers (<see cref="ManagerGraph"/>) from one or more to zero. The break-glass administrator is not a
/// manager here, because it is a configuration-held emergency account; when the caller confirms
/// (<c>confirmBreakGlassOnly</c>) and an active break-glass account exists, the write is allowed with the warning
/// <see cref="AccessErrorCodes.OnlyBreakGlassManages"/>. A write that changes no manager key, or a world that had no
/// manager before (bootstrapping by break-glass), is always allowed. Reads only; never writes.
/// </summary>
public sealed class ManagerContinuityGuard(IManagerGraphReader reader, IAccountAdministration accounts)
{
    /// <summary>
    /// The page size of the account reads (a small page: one account that is neither break-glass nor the account being
    /// switched off answers the question).
    /// </summary>
    public const int ProbePageSize = 3;

    /// <summary>The page size of the break-glass scan.</summary>
    public const int BreakGlassScanPageSize = 100;

    /// <summary>The most pages the break-glass scan reads before it reports "not available" (fail closed).</summary>
    public const int BreakGlassScanMaxPages = 50;

    private const int FirstPage = 1;

    /// <summary>The verdict on replacing one user's roles and direct grants.</summary>
    public async Task<GuardVerdict> CheckAsync(UserGrantChange change, bool confirmBreakGlassOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        var before = await reader.ReadAsync(ManagerGraph.ManagerKeys, cancellationToken);
        var after = before.WithCandidate(new ManagerCandidate(
            UserId: change.UserId,
            RoleIds: change.RoleIds.ToHashSet(),
            DirectKeys: ManagerGraph.ManagerSubset(change.Keys)));
        if (string.Equals(before.Footprint(change.UserId), after.Footprint(change.UserId), StringComparison.Ordinal))
        {
            return GuardVerdict.Allowed;
        }

        return await DecideAsync(before: before, after: after, confirmBreakGlassOnly, cancellationToken);
    }

    /// <summary>The verdict on changing one role's active state and permissions.</summary>
    public async Task<GuardVerdict> CheckAsync(RoleChange change, bool confirmBreakGlassOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        var before = await reader.ReadAsync(ManagerGraph.ManagerKeys, cancellationToken);
        var after = before.WithRole(change.RoleId, change.IsActive, change.Keys);
        if (string.Equals(before.RoleFootprint(change.RoleId), after.RoleFootprint(change.RoleId), StringComparison.Ordinal))
        {
            return GuardVerdict.Allowed;
        }

        return await DecideAsync(before: before, after: after, confirmBreakGlassOnly, cancellationToken);
    }

    /// <summary>
    /// The verdict on switching off user <paramref name="userId"/> (a deactivation): <see cref="GuardVerdict.Refused"/>
    /// when that user is the last manager. There is no break-glass confirmation here: hand the manager keys to someone
    /// else first.
    /// </summary>
    public async Task<GuardVerdict> CheckDeactivationAsync(long userId, CancellationToken cancellationToken)
    {
        var graph = await reader.ReadAsync(ManagerGraph.ManagerKeys, cancellationToken);
        if (await AnyManagerAsync(graph, excludedUserId: userId, cancellationToken) || !await AnyManagerAsync(graph, excludedUserId: null, cancellationToken))
        {
            return GuardVerdict.Allowed;
        }

        return GuardVerdict.Refused;
    }

    private async Task<GuardVerdict> DecideAsync(ManagerGraph before, ManagerGraph after, bool confirmBreakGlassOnly, CancellationToken cancellationToken)
    {
        if (await AnyManagerAsync(after, excludedUserId: null, cancellationToken) || !await AnyManagerAsync(before, excludedUserId: null, cancellationToken))
        {
            return GuardVerdict.Allowed;
        }

        return confirmBreakGlassOnly && await BreakGlassAvailableAsync(cancellationToken)
            ? GuardVerdict.AllowedBreakGlassOnly
            : GuardVerdict.Refused;
    }

    private async Task<bool> AnyManagerAsync(ManagerGraph graph, long? excludedUserId, CancellationToken cancellationToken)
    {
        foreach (var candidate in graph.Candidates.Where(candidate => candidate.UserId != excludedUserId))
        {
            var account = await accounts.FindAsync(candidate.UserId, cancellationToken);
            if (account is not { IsActive: true, IsBreakGlass: false })
            {
                continue;
            }

            var keys = new HashSet<string>(graph.RowKeys(candidate), StringComparer.Ordinal);
            keys.UnionWith(graph.VirtualKeys(account.Kind));
            if (ManagerGraph.IsComplete(keys))
            {
                return true;
            }
        }

        foreach (var kind in Enum.GetValues<UserKind>().Where(kind => ManagerGraph.IsComplete(graph.VirtualKeys(kind))))
        {
            if (await AnyActiveAccountOfKindAsync(kind, excludedUserId, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> AnyActiveAccountOfKindAsync(UserKind kind, long? excludedUserId, CancellationToken cancellationToken)
    {
        var page = await accounts.ListAsync(
            new AccountQuery(Search: null, OrganizationId: null, Kind: kind, IsActive: true, Page: FirstPage, PageSize: ProbePageSize),
            cancellationToken);
        return page.Items.Any(account => !account.IsBreakGlass && account.UserId != excludedUserId);
    }

    private async Task<bool> BreakGlassAvailableAsync(CancellationToken cancellationToken)
    {
        for (var pageNumber = FirstPage; pageNumber <= BreakGlassScanMaxPages; pageNumber++)
        {
            var page = await accounts.ListAsync(
                new AccountQuery(Search: null, OrganizationId: null, Kind: null, IsActive: true, Page: pageNumber, PageSize: BreakGlassScanPageSize),
                cancellationToken);
            if (page.Items.Any(account => account.IsBreakGlass))
            {
                return true;
            }

            if (page.Items.Count == 0 || pageNumber * BreakGlassScanPageSize >= page.Total)
            {
                return false;
            }
        }

        return false;
    }
}

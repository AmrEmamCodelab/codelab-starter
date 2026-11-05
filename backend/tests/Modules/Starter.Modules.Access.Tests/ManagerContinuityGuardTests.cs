using Starter.Modules.Access.Application;
using Starter.Modules.Access.Domain;
using Starter.Modules.Access.Tests.Fakes;

namespace Starter.Modules.Access.Tests;

public sealed class ManagerContinuityGuardTests
{
    private const long Manager = 1;
    private const long Other = 2;
    private const long BreakGlass = 5000;
    private const long Outsider = 70;
    private static readonly string[] ManagerKeys = [AccessPermissionKeys.RolesManage, AccessPermissionKeys.GrantsManage];

    private readonly AccessStore _store = new AccessStore().WithPermissions(TestCatalogs.FullKeys);
    private readonly FakeAccounts _accounts = new();

    public ManagerContinuityGuardTests()
    {
        _accounts.Add(Manager);
        _accounts.Add(Other);
        _accounts.Add(BreakGlass, isBreakGlass: true);
    }

    [Fact]
    public async Task Removing_the_last_managers_direct_grants_is_refused()
    {
        GrantManager(Manager);

        var verdict = await Guard().CheckAsync(new UserGrantChange(Manager, [], []), confirmBreakGlassOnly: false, CancellationToken.None);

        Assert.Equal(GuardVerdict.Refused, verdict);
    }

    [Fact]
    public async Task Confirming_while_break_glass_exists_allows_with_the_warning_verdict()
    {
        GrantManager(Manager);

        var verdict = await Guard().CheckAsync(new UserGrantChange(Manager, [], []), confirmBreakGlassOnly: true, CancellationToken.None);

        Assert.Equal(GuardVerdict.AllowedBreakGlassOnly, verdict);
        Assert.Equal([AccessErrorCodes.OnlyBreakGlassManages], GuardWarnings.Of(verdict));
    }

    [Fact]
    public async Task Confirming_without_an_active_break_glass_account_is_still_refused()
    {
        _accounts.Accounts.RemoveAll(account => account.IsBreakGlass);
        GrantManager(Manager);

        Assert.Equal(GuardVerdict.Refused, await Guard().CheckAsync(new UserGrantChange(Manager, [], []), confirmBreakGlassOnly: true, CancellationToken.None));
    }

    [Fact]
    public async Task Removing_one_manager_while_another_remains_is_allowed()
    {
        GrantManager(Manager);
        GrantManager(Other);

        Assert.Equal(GuardVerdict.Allowed, await Guard().CheckAsync(new UserGrantChange(Manager, [], []), confirmBreakGlassOnly: false, CancellationToken.None));
    }

    [Fact]
    public async Task A_change_that_touches_no_manager_key_is_allowed_without_reading_accounts()
    {
        GrantManager(Manager);

        var verdict = await Guard().CheckAsync(new UserGrantChange(Other, [], [AccessPermissionKeys.DashboardView]), confirmBreakGlassOnly: false, CancellationToken.None);

        Assert.Equal(GuardVerdict.Allowed, verdict);
        Assert.Equal(0, _accounts.Finds);
    }

    [Fact]
    public async Task With_no_manager_before_any_change_is_allowed()
    {
        _store.GrantDirect(Manager, AccessPermissionKeys.RolesManage);

        Assert.Equal(GuardVerdict.Allowed, await Guard().CheckAsync(new UserGrantChange(Manager, [], []), confirmBreakGlassOnly: false, CancellationToken.None));
    }

    [Fact]
    public async Task Holding_only_one_of_the_two_keys_does_not_make_a_manager()
    {
        GrantManager(Manager);
        _store.GrantDirect(Other, AccessPermissionKeys.RolesManage);

        Assert.Equal(GuardVerdict.Refused, await Guard().CheckAsync(new UserGrantChange(Manager, [], []), confirmBreakGlassOnly: false, CancellationToken.None));
    }

    [Fact]
    public async Task An_inactive_or_break_glass_holder_does_not_count()
    {
        GrantManager(Manager);
        GrantManager(BreakGlass);
        _accounts.Add(3, isActive: false);
        GrantManager(3);

        Assert.Equal(GuardVerdict.Refused, await Guard().CheckAsync(new UserGrantChange(Manager, [], []), confirmBreakGlassOnly: false, CancellationToken.None));
    }

    [Fact]
    public async Task Deactivating_or_stripping_the_only_manager_role_is_refused()
    {
        var admins = _store.AddRole("admins", ManagerKeys);
        _store.Assign(Manager, admins);

        Assert.Equal(GuardVerdict.Refused, await Guard().CheckAsync(new RoleChange(admins.Id, IsActive: false, ManagerKeys), confirmBreakGlassOnly: false, CancellationToken.None));
        Assert.Equal(GuardVerdict.Refused, await Guard().CheckAsync(new RoleChange(admins.Id, IsActive: true, [AccessPermissionKeys.RolesManage]), confirmBreakGlassOnly: false, CancellationToken.None));
        Assert.Equal(GuardVerdict.Allowed, await Guard().CheckAsync(new RoleChange(admins.Id, IsActive: true, [.. ManagerKeys, AccessPermissionKeys.DashboardView]), confirmBreakGlassOnly: false, CancellationToken.None));
    }

    [Fact]
    public async Task Unassigning_the_manager_role_is_refused_but_moving_it_to_direct_grants_is_allowed()
    {
        var admins = _store.AddRole("admins", ManagerKeys);
        _store.Assign(Manager, admins);

        Assert.Equal(GuardVerdict.Refused, await Guard().CheckAsync(new UserGrantChange(Manager, [], []), confirmBreakGlassOnly: false, CancellationToken.None));
        Assert.Equal(GuardVerdict.Allowed, await Guard().CheckAsync(new UserGrantChange(Manager, [], ManagerKeys), confirmBreakGlassOnly: false, CancellationToken.None));
    }

    [Fact]
    public async Task A_virtual_role_with_both_keys_keeps_a_manager_while_an_active_user_of_that_kind_exists()
    {
        GrantManager(Manager);
        _store.AddVirtualRole(VirtualRoleKind.AllInternalUsers, ManagerKeys);

        Assert.Equal(GuardVerdict.Allowed, await Guard().CheckAsync(new UserGrantChange(Manager, [], []), confirmBreakGlassOnly: false, CancellationToken.None));
    }

    [Fact]
    public async Task A_virtual_role_of_a_kind_with_no_active_user_does_not_keep_a_manager()
    {
        GrantManager(Manager);
        _store.AddVirtualRole(VirtualRoleKind.AllExternalUsers, ManagerKeys);

        Assert.Equal(GuardVerdict.Refused, await Guard().CheckAsync(new UserGrantChange(Manager, [], []), confirmBreakGlassOnly: false, CancellationToken.None));
    }

    [Fact]
    public async Task A_virtual_role_can_complete_a_candidates_partial_rows()
    {
        GrantManager(Manager);
        _store.GrantDirect(Other, AccessPermissionKeys.RolesManage);
        _store.AddVirtualRole(VirtualRoleKind.AllAuthenticatedUsers, AccessPermissionKeys.GrantsManage);

        Assert.Equal(GuardVerdict.Allowed, await Guard().CheckAsync(new UserGrantChange(Manager, [], []), confirmBreakGlassOnly: false, CancellationToken.None));
    }

    [Fact]
    public async Task The_break_glass_scan_reads_further_pages()
    {
        for (var userId = 1000; userId < 1000 + ManagerContinuityGuard.BreakGlassScanPageSize; userId++)
        {
            _accounts.Add(userId);
        }

        GrantManager(Manager);

        Assert.Equal(GuardVerdict.AllowedBreakGlassOnly, await Guard().CheckAsync(new UserGrantChange(Manager, [], []), confirmBreakGlassOnly: true, CancellationToken.None));
        Assert.Contains(_accounts.Queries, query => query.Page == 2);
    }

    [Fact]
    public async Task Deactivating_the_last_manager_is_refused_and_another_manager_allows_it()
    {
        GrantManager(Manager);

        Assert.Equal(GuardVerdict.Refused, await Guard().CheckDeactivationAsync(Manager, CancellationToken.None));
        Assert.Equal(GuardVerdict.Allowed, await Guard().CheckDeactivationAsync(Other, CancellationToken.None));

        GrantManager(Other);

        Assert.Equal(GuardVerdict.Allowed, await Guard().CheckDeactivationAsync(Manager, CancellationToken.None));
    }

    [Fact]
    public async Task Deactivating_the_only_user_a_manager_virtual_role_reaches_is_refused()
    {
        _store.AddVirtualRole(VirtualRoleKind.AllExternalUsers, ManagerKeys);
        _accounts.Add(Outsider, kind: FakeAccounts.SecondKind);

        Assert.Equal(GuardVerdict.Refused, await Guard().CheckDeactivationAsync(Outsider, CancellationToken.None));

        _accounts.Add(Outsider + 1, kind: FakeAccounts.SecondKind);

        Assert.Equal(GuardVerdict.Allowed, await Guard().CheckDeactivationAsync(Outsider, CancellationToken.None));
    }

    [Fact]
    public void The_graph_helpers_keep_only_manager_keys()
    {
        Assert.Equal(ManagerKeys.Order(StringComparer.Ordinal), ManagerGraph.ManagerSubset([.. ManagerKeys, AccessPermissionKeys.DashboardView]).Order(StringComparer.Ordinal));
        Assert.True(ManagerGraph.IsComplete(new HashSet<string>(ManagerKeys, StringComparer.Ordinal)));
        Assert.False(ManagerGraph.IsComplete(new HashSet<string>([AccessPermissionKeys.RolesManage], StringComparer.Ordinal)));
        Assert.Equal(string.Empty, new ManagerGraph([], []).Footprint(Manager));
        Assert.Equal(string.Empty, new ManagerGraph([], []).RoleFootprint(1));
    }

    private void GrantManager(long userId)
    {
        foreach (var key in ManagerKeys)
        {
            _store.GrantDirect(userId, key);
        }
    }

    private ManagerContinuityGuard Guard() => new(_store, _accounts);
}

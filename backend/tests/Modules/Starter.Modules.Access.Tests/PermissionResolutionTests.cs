using Starter.Application.Authorization;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Domain;
using Starter.Modules.Access.Tests.Fakes;

namespace Starter.Modules.Access.Tests;

public sealed class PermissionResolutionTests
{
    private const string Retired = "retired.key";
    private static readonly AccountFacts InternalUser = new(IsActive: true, IsBreakGlass: false, Kind: FakeAccounts.FirstKind);
    private static readonly AccountFacts ExternalUser = new(IsActive: true, IsBreakGlass: false, Kind: FakeAccounts.SecondKind);
    private readonly PermissionCatalog _catalog = TestCatalogs.Default();

    [Fact]
    public void An_inactive_user_holds_nothing_even_as_break_glass()
    {
        var facts = new AccountFacts(IsActive: false, IsBreakGlass: true, Kind: FakeAccounts.FirstKind);

        Assert.Empty(PermissionResolution.Resolve(facts, snapshot: null, _catalog));
        Assert.False(PermissionResolution.NeedsGrants(facts));
    }

    [Fact]
    public void Break_glass_holds_the_whole_catalog_globally_without_reading_grants()
    {
        var facts = new AccountFacts(IsActive: true, IsBreakGlass: true, Kind: FakeAccounts.FirstKind);

        var held = PermissionResolution.Resolve(facts, snapshot: null, _catalog);

        Assert.False(PermissionResolution.NeedsGrants(facts));
        Assert.Equal(_catalog.Keys.Order(StringComparer.Ordinal), held.Select(grant => grant.Key));
        Assert.All(held, grant => Assert.Equal(PermissionScope.Global, grant.Scope));
        Assert.All(held, grant => Assert.Equal(PermissionSourceKind.BreakGlass, Assert.Single(grant.Sources).Kind));
    }

    [Fact]
    public void The_union_of_direct_grants_assigned_roles_and_matching_virtual_roles()
    {
        var snapshot = new UserGrantSnapshot(
            Direct: [new DirectGrant(AccessPermissionKeys.DashboardView, PermissionScope.Global)],
            Assigned: [new AssignedRole(Role(1, AccessPermissionKeys.AccessAdminView), PermissionScope.OwnOrganization)],
            VirtualRoles: [Virtual(2, VirtualRoleKind.AllInternalUsers, TestCatalogs.ReportsView)]);

        var held = PermissionResolution.Resolve(InternalUser, snapshot, _catalog);

        Assert.Equal([AccessPermissionKeys.AccessAdminView, AccessPermissionKeys.DashboardView, TestCatalogs.ReportsView], held.Select(grant => grant.Key));
        Assert.Equal(PermissionSourceKind.Role, Single(held, AccessPermissionKeys.AccessAdminView).Sources[0].Kind);
        Assert.Equal(1, Single(held, AccessPermissionKeys.AccessAdminView).Sources[0].RoleId);
        Assert.Equal(PermissionSourceKind.VirtualRole, Single(held, TestCatalogs.ReportsView).Sources[0].Kind);
    }

    [Fact]
    public void Retired_keys_inactive_roles_and_virtual_roles_of_another_kind_grant_nothing()
    {
        var inactive = Role(1, AccessPermissionKeys.AccessAdminView) with { IsActive = false };
        var snapshot = new UserGrantSnapshot(
            Direct: [new DirectGrant(Retired, PermissionScope.Global)],
            Assigned: [new AssignedRole(inactive, PermissionScope.Global)],
            VirtualRoles:
            [
                Virtual(2, VirtualRoleKind.AllExternalUsers, AccessPermissionKeys.DashboardView),
                Virtual(3, VirtualRoleKind.AllAuthenticatedUsers, TestCatalogs.ReportsView) with { IsActive = false },
            ]);

        Assert.Empty(PermissionResolution.Resolve(InternalUser, snapshot, _catalog));
    }

    [Fact]
    public void An_assigned_virtual_role_row_is_ignored()
    {
        var snapshot = new UserGrantSnapshot(
            Direct: [],
            Assigned: [new AssignedRole(Virtual(1, VirtualRoleKind.AllExternalUsers, AccessPermissionKeys.DashboardView), PermissionScope.Global)],
            VirtualRoles: []);

        Assert.Empty(PermissionResolution.Resolve(InternalUser, snapshot, _catalog));
    }

    [Fact]
    public void Global_wins_over_own_organization_and_every_source_is_listed()
    {
        var snapshot = new UserGrantSnapshot(
            Direct: [new DirectGrant(AccessPermissionKeys.DashboardView, PermissionScope.OwnOrganization)],
            Assigned: [new AssignedRole(Role(5, AccessPermissionKeys.DashboardView), PermissionScope.Global)],
            VirtualRoles: [Virtual(6, VirtualRoleKind.AllExternalUsers, AccessPermissionKeys.DashboardView)]);

        var dashboard = Single(PermissionResolution.Resolve(ExternalUser, snapshot, _catalog), AccessPermissionKeys.DashboardView);

        Assert.Equal(PermissionScope.Global, dashboard.Scope);
        Assert.Equal([PermissionSourceKind.Direct, PermissionSourceKind.Role, PermissionSourceKind.VirtualRole], dashboard.Sources.Select(source => source.Kind));
    }

    [Fact]
    public void Own_organization_only_sources_stay_own_organization_and_virtual_roles_grant_own_organization()
    {
        var snapshot = new UserGrantSnapshot(
            Direct: [new DirectGrant(AccessPermissionKeys.DashboardView, PermissionScope.OwnOrganization)],
            Assigned: [],
            VirtualRoles: [Virtual(6, VirtualRoleKind.AllAuthenticatedUsers, AccessPermissionKeys.DashboardView, TestCatalogs.ReportsView)]);

        var held = PermissionResolution.Resolve(ExternalUser, snapshot, _catalog);

        Assert.All(held, grant => Assert.Equal(PermissionScope.OwnOrganization, grant.Scope));
        Assert.Equal(PermissionResolution.VirtualRoleScope, Single(held, TestCatalogs.ReportsView).Scope);
    }

    [Theory]
    [InlineData(PermissionScope.Global, PermissionScope.Global, PermissionScope.Global)]
    [InlineData(PermissionScope.Global, PermissionScope.OwnOrganization, PermissionScope.Global)]
    [InlineData(PermissionScope.OwnOrganization, PermissionScope.Global, PermissionScope.Global)]
    [InlineData(PermissionScope.OwnOrganization, PermissionScope.OwnOrganization, PermissionScope.OwnOrganization)]
    public void Widest_prefers_global(PermissionScope first, PermissionScope second, PermissionScope expected) =>
        Assert.Equal(expected, PermissionResolution.Widest(first, second));

    [Fact]
    public void An_active_user_needs_a_snapshot() =>
        Assert.Throws<ArgumentNullException>(() => PermissionResolution.Resolve(InternalUser, snapshot: null, _catalog));

    private static EffectiveGrant Single(IReadOnlyList<EffectiveGrant> held, string key) => Assert.Single(held, grant => grant.Key == key);

    private static RoleGrants Role(long id, params string[] keys) =>
        new(RoleId: id, Code: "role-" + id, NameEn: "Role " + id, NameAr: "دور " + id, IsActive: true, VirtualKind: null, Keys: keys);

    private static RoleGrants Virtual(long id, VirtualRoleKind kind, params string[] keys) => Role(id, keys) with { VirtualKind = kind };
}

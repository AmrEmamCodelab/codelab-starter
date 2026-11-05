using Starter.Application.Authorization;
using Starter.Application.Users;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Domain;
using Starter.Modules.Access.Tests.Fakes;

namespace Starter.Modules.Access.Tests;

public sealed class UserPermissionResolverTests
{
    private const long UserId = 42;
    private const long OrganizationId = 7;
    private static readonly Guid Subject = Guid.Parse("7f1e4c1a-3b0d-4a43-9f1e-2c6b1d1a0001");

    private readonly FakeUserDirectory _directory = new();
    private readonly AccessStore _store = new AccessStore().WithPermissions(TestCatalogs.FullKeys).WithPermissions("retired.key");
    private readonly PermissionCatalog _catalog = TestCatalogs.Full();

    [Fact]
    public async Task An_unknown_user_holds_nothing()
    {
        var access = await Resolver().ResolveAccessAsync(Subject, CancellationToken.None);

        Assert.Null(access.User);
        Assert.Empty(access.Permissions);
        Assert.Empty(await Resolver().ResolveAsync(Subject, CancellationToken.None));
    }

    [Fact]
    public async Task An_inactive_user_holds_nothing_even_with_grants_or_all_permissions()
    {
        _directory.Add(Subject, new DirectoryUser(UserId: UserId, IsActive: false, HoldsAllPermissions: true));
        _store.GrantDirect(UserId, AccessPermissionKeys.DashboardView);

        Assert.Empty(await Resolver().ResolveAsync(Subject, CancellationToken.None));
    }

    [Fact]
    public async Task A_user_holding_all_permissions_gets_the_whole_catalog()
    {
        _directory.Add(Subject, new DirectoryUser(UserId: UserId, IsActive: true, HoldsAllPermissions: true));

        var permissions = await Resolver().ResolveAsync(Subject, CancellationToken.None);

        Assert.Equal(_catalog.Keys.Order(StringComparer.Ordinal), permissions.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_active_user_gets_direct_role_and_virtual_grants_still_in_the_catalog()
    {
        _directory.Add(Subject, new DirectoryUser(UserId: UserId, IsActive: true, HoldsAllPermissions: false, Kind: FakeAccounts.SecondKind, OrganizationId: OrganizationId));
        _store.GrantDirect(UserId, AccessPermissionKeys.DashboardView);
        _store.GrantDirect(UserId, "retired.key");
        _store.Assign(UserId, _store.AddRole("viewers", AccessPermissionKeys.RolesView), PermissionScope.OwnOrganization);
        _store.AddVirtualRole(VirtualRoleKind.AllExternalUsers, TestCatalogs.ReportsView);
        _store.AddVirtualRole(VirtualRoleKind.AllInternalUsers, AccessPermissionKeys.GrantsManage);

        var access = await Resolver().ResolveAccessAsync(Subject, CancellationToken.None);

        Assert.Equal(OrganizationId, access.User!.OrganizationId);
        Assert.Equal([AccessPermissionKeys.RolesView, AccessPermissionKeys.DashboardView, TestCatalogs.ReportsView], access.Permissions.Select(grant => grant.Key));
        Assert.Equal(PermissionScope.OwnOrganization, access.Find(AccessPermissionKeys.RolesView)!.Scope);
        Assert.Null(access.Find(AccessPermissionKeys.GrantsManage));
    }

    [Fact]
    public async Task Another_users_grants_do_not_leak()
    {
        _directory.Add(Subject, new DirectoryUser(UserId: UserId + 1, IsActive: true, HoldsAllPermissions: false));
        _store.GrantDirect(UserId, AccessPermissionKeys.DashboardView);

        Assert.Empty(await Resolver().ResolveAsync(Subject, CancellationToken.None));
    }

    [Fact]
    public async Task Every_call_resolves_live_so_a_grant_and_a_revocation_bite_on_the_next_call()
    {
        _directory.Add(Subject, new DirectoryUser(UserId: UserId, IsActive: true, HoldsAllPermissions: false));
        var resolver = Resolver();
        var role = _store.AddRole("viewers", AccessPermissionKeys.DashboardView);
        Assert.Empty(await resolver.ResolveAsync(Subject, CancellationToken.None));

        _store.Assign(UserId, role);
        Assert.Contains(AccessPermissionKeys.DashboardView, await resolver.ResolveAsync(Subject, CancellationToken.None));

        role.SetActive(false, AccessStore.Epoch);
        Assert.Empty(await resolver.ResolveAsync(Subject, CancellationToken.None));
        Assert.Equal(3, _directory.Lookups);
    }

    private UserPermissionResolver Resolver() => new(_directory, _store, _catalog);
}

public sealed class OrganizationScopeTests
{
    private const long OrganizationId = 9;
    private static readonly Guid Subject = Guid.Parse("7f1e4c1a-3b0d-4a43-9f1e-2c6b1d1a0009");
    private static readonly DirectoryUser User = new(UserId: 3, IsActive: true, HoldsAllPermissions: false, OrganizationId: OrganizationId);

    [Fact]
    public async Task A_global_holder_gets_a_global_decision_without_an_organization()
    {
        var scope = new CurrentUserOrganizationScope(new FakeCurrentUser(Subject), new FakeResolver(AccessPermissionKeys.DashboardView) { User = User });

        var decision = await scope.ScopeForAsync(AccessPermissionKeys.DashboardView, CancellationToken.None);

        Assert.Equal(new ScopeDecision(Allowed: true, Scope: PermissionScope.Global, OrganizationId: null), decision);
    }

    [Fact]
    public async Task An_own_organization_holder_gets_their_organization()
    {
        var resolver = new FakeResolver(AccessPermissionKeys.DashboardView) { User = User, Scope = PermissionScope.OwnOrganization };

        var decision = await new CurrentUserOrganizationScope(new FakeCurrentUser(Subject), resolver).ScopeForAsync(AccessPermissionKeys.DashboardView, CancellationToken.None);

        Assert.Equal(new ScopeDecision(Allowed: true, Scope: PermissionScope.OwnOrganization, OrganizationId: OrganizationId), decision);
    }

    [Fact]
    public async Task A_key_the_user_does_not_hold_and_a_caller_without_subject_are_denied()
    {
        var resolver = new FakeResolver(AccessPermissionKeys.DashboardView) { User = User };

        Assert.Same(ScopeDecision.Denied, await new CurrentUserOrganizationScope(new FakeCurrentUser(Subject), resolver).ScopeForAsync(AccessPermissionKeys.RolesView, CancellationToken.None));
        Assert.Same(ScopeDecision.Denied, await new CurrentUserOrganizationScope(new FakeCurrentUser(subjectId: null), resolver).ScopeForAsync(AccessPermissionKeys.DashboardView, CancellationToken.None));
        Assert.Equal(1, resolver.Calls);
    }

    [Fact]
    public async Task A_blank_key_is_a_programming_error() =>
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            new CurrentUserOrganizationScope(new FakeCurrentUser(Subject), new FakeResolver()).ScopeForAsync(" ", CancellationToken.None));
}

using Starter.Application.Authorization;
using Starter.Application.Users;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Domain;
using Starter.Modules.Access.Tests.Fakes;

namespace Starter.Modules.Access.Tests;

public sealed class GrantCeilingTests
{
    private const long CallerUserId = 1;
    private const long TargetUserId = 7;
    private const long CallerOrganization = 5;
    private const long OtherOrganization = 6;
    private static readonly Guid CallerSubject = Guid.Parse("5c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e01");

    private readonly AccessStore _store = new AccessStore().WithPermissions(TestCatalogs.FullKeys);
    private readonly FakeUserDirectory _directory = new();
    private readonly FakeAccounts _accounts = new();
    private readonly AccountSummary _colleague;

    public GrantCeilingTests()
    {
        _directory.Add(CallerSubject, new DirectoryUser(UserId: CallerUserId, IsActive: true, HoldsAllPermissions: false, OrganizationId: CallerOrganization));
        _colleague = _accounts.Add(TargetUserId, organizationId: CallerOrganization);
    }

    [Fact]
    public async Task An_own_organization_grants_manager_assigns_roles_at_own_organization_scope_only()
    {
        _store.GrantDirect(CallerUserId, AccessPermissionKeys.GrantsManage, PermissionScope.OwnOrganization);
        var role = _store.AddRole("team", AccessPermissionKeys.DashboardView);

        Assert.True(await Ceiling().PermitsAsync(_colleague, Request(roles: [(role, PermissionScope.OwnOrganization)]), CancellationToken.None));
        Assert.False(await Ceiling().PermitsAsync(_colleague, Request(roles: [(role, PermissionScope.Global)]), CancellationToken.None));
    }

    [Fact]
    public async Task A_direct_grant_needs_the_caller_to_hold_the_key_at_least_as_widely()
    {
        _store.GrantDirect(CallerUserId, AccessPermissionKeys.GrantsManage, PermissionScope.Global);
        _store.GrantDirect(CallerUserId, AccessPermissionKeys.DashboardView, PermissionScope.OwnOrganization);

        Assert.True(await Ceiling().PermitsAsync(_colleague, Grant(AccessPermissionKeys.DashboardView, PermissionScope.OwnOrganization), CancellationToken.None));
        Assert.False(await Ceiling().PermitsAsync(_colleague, Grant(AccessPermissionKeys.DashboardView, PermissionScope.Global), CancellationToken.None));
        Assert.False(await Ceiling().PermitsAsync(_colleague, Grant(AccessPermissionKeys.RolesManage, PermissionScope.OwnOrganization), CancellationToken.None));
    }

    [Fact]
    public async Task An_own_organization_key_covers_a_grant_in_the_callers_organization_only()
    {
        _store.GrantDirect(CallerUserId, AccessPermissionKeys.GrantsManage, PermissionScope.Global);
        _store.GrantDirect(CallerUserId, AccessPermissionKeys.DashboardView, PermissionScope.OwnOrganization);
        var stranger = _accounts.Add(TargetUserId + 1, organizationId: OtherOrganization);

        Assert.False(await Ceiling().PermitsAsync(stranger, Grant(AccessPermissionKeys.DashboardView, PermissionScope.OwnOrganization), CancellationToken.None));
    }

    [Fact]
    public async Task An_own_organization_grants_manager_cannot_give_global_grants_manage_to_anyone()
    {
        _store.GrantDirect(CallerUserId, AccessPermissionKeys.GrantsManage, PermissionScope.OwnOrganization);

        Assert.False(await Ceiling().PermitsAsync(_colleague, Grant(AccessPermissionKeys.GrantsManage, PermissionScope.Global), CancellationToken.None));
        Assert.True(await Ceiling().PermitsAsync(_colleague, Grant(AccessPermissionKeys.GrantsManage, PermissionScope.OwnOrganization), CancellationToken.None));
    }

    [Fact]
    public async Task The_caller_never_raises_their_own_grants_but_may_keep_or_narrow_them()
    {
        var self = _accounts.Add(CallerUserId, organizationId: CallerOrganization) with { SubjectId = CallerSubject };
        _store.GrantDirect(CallerUserId, AccessPermissionKeys.GrantsManage, PermissionScope.Global);
        _store.GrantDirect(CallerUserId, AccessPermissionKeys.DashboardView, PermissionScope.OwnOrganization);

        var widen = await Ceiling().PermitsAsync(self, Request(permissions: [(AccessPermissionKeys.GrantsManage, PermissionScope.Global), (AccessPermissionKeys.DashboardView, PermissionScope.Global)]), CancellationToken.None);
        var add = await Ceiling().PermitsAsync(self, Request(permissions: [(AccessPermissionKeys.GrantsManage, PermissionScope.Global), (AccessPermissionKeys.RolesView, PermissionScope.OwnOrganization)]), CancellationToken.None);
        var keep = await Ceiling().PermitsAsync(self, Request(permissions: [(AccessPermissionKeys.GrantsManage, PermissionScope.Global), (AccessPermissionKeys.DashboardView, PermissionScope.OwnOrganization)]), CancellationToken.None);
        var narrow = await Ceiling().PermitsAsync(self, Grant(AccessPermissionKeys.GrantsManage, PermissionScope.OwnOrganization), CancellationToken.None);

        Assert.False(widen);
        Assert.False(add);
        Assert.True(keep);
        Assert.True(narrow);
    }

    [Fact]
    public async Task Entries_the_target_already_holds_stay_whoever_granted_them()
    {
        _store.GrantDirect(CallerUserId, AccessPermissionKeys.GrantsManage, PermissionScope.OwnOrganization);
        var role = _store.AddRole("wide", AccessPermissionKeys.RolesView);
        _store.Assign(TargetUserId, role, PermissionScope.Global);
        _store.GrantDirect(TargetUserId, AccessPermissionKeys.RolesManage, PermissionScope.Global);

        var request = Request(roles: [(role, PermissionScope.Global)], permissions: [(AccessPermissionKeys.RolesManage, PermissionScope.Global)]);

        Assert.True(await Ceiling().PermitsAsync(_colleague, request, CancellationToken.None));
    }

    [Fact]
    public async Task An_anonymous_caller_or_one_without_grants_manage_raises_nothing()
    {
        _store.GrantDirect(CallerUserId, AccessPermissionKeys.DashboardView, PermissionScope.Global);
        var request = Grant(AccessPermissionKeys.DashboardView, PermissionScope.OwnOrganization);

        Assert.False(await Ceiling().PermitsAsync(_colleague, request, CancellationToken.None));
        Assert.False(await Ceiling(new FakeCurrentUser(subjectId: null)).PermitsAsync(_colleague, request, CancellationToken.None));
        Assert.False(await Ceiling(new FakeCurrentUser(subjectId: null)).PermitsAsync(_colleague, Request(), CancellationToken.None));
    }

    [Fact]
    public async Task A_global_grants_manager_holding_the_key_globally_grants_it_globally()
    {
        _store.GrantDirect(CallerUserId, AccessPermissionKeys.GrantsManage, PermissionScope.Global);
        _store.GrantDirect(CallerUserId, AccessPermissionKeys.DashboardView, PermissionScope.Global);
        var role = _store.AddRole("team", AccessPermissionKeys.RolesView);

        var request = Request(roles: [(role, PermissionScope.Global)], permissions: [(AccessPermissionKeys.DashboardView, PermissionScope.Global)]);

        Assert.True(await Ceiling().PermitsAsync(_colleague, request, CancellationToken.None));
    }

    private GrantRequest Grant(string key, PermissionScope scope) => Request(permissions: [(key, scope)]);

    private GrantRequest Request(
        IReadOnlyList<(Role Role, PermissionScope Scope)>? roles = null,
        IReadOnlyList<(string Key, PermissionScope Scope)>? permissions = null) =>
        new(
            Roles: roles ?? [],
            Permissions: [.. (permissions ?? []).Select(entry => (_store.Permission(entry.Key), entry.Scope))]);

    private GrantCeiling Ceiling() => Ceiling(new FakeCurrentUser(CallerSubject));

    private GrantCeiling Ceiling(FakeCurrentUser currentUser) =>
        new(currentUser, new UserPermissionResolver(_directory, _store, TestCatalogs.Full()), _store);
}

using Starter.Application.Authorization;
using Starter.Application.Users;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Tests.Fakes;

namespace Starter.Modules.Access.Tests;

public sealed class AdministrationGuardTests
{
    private const long CallerUserId = 1;
    private const long TargetUserId = 7;
    private const long CallerOrganization = 5;
    private const long OtherOrganization = 6;
    private static readonly Guid CallerSubject = Guid.Parse("0b6a3c1e-4d2f-4c8e-9a10-5e7d3f2a1b01");
    private static readonly Guid TargetSubject = Guid.Parse("0b6a3c1e-4d2f-4c8e-9a10-5e7d3f2a1b02");

    private readonly AccessStore _store = new AccessStore().WithPermissions(TestCatalogs.FullKeys);
    private readonly FakeUserDirectory _directory = new();
    private readonly FakeAccounts _accounts = new();

    public AdministrationGuardTests() =>
        _directory.Add(CallerSubject, new DirectoryUser(UserId: CallerUserId, IsActive: true, HoldsAllPermissions: false, OrganizationId: CallerOrganization));

    [Fact]
    public async Task A_caller_covers_a_target_whose_permissions_they_hold()
    {
        AddTarget(isActive: true);
        _store.GrantDirect(TargetUserId, AccessPermissionKeys.DashboardView);
        _store.GrantDirect(CallerUserId, AccessPermissionKeys.DashboardView);
        _store.GrantDirect(CallerUserId, AccessPermissionKeys.GrantsView);

        Assert.True(await Guard().CallerCoversAsync(TargetSubject, CancellationToken.None));
    }

    [Fact]
    public async Task A_caller_does_not_cover_a_target_holding_a_key_they_lack()
    {
        AddTarget(isActive: true);
        _store.GrantDirect(TargetUserId, AccessPermissionKeys.GrantsManage);
        _store.GrantDirect(CallerUserId, AccessPermissionKeys.DashboardView);

        Assert.False(await Guard().CallerCoversAsync(TargetSubject, CancellationToken.None));
    }

    [Fact]
    public async Task A_deactivated_target_is_judged_by_what_it_would_hold_when_reactivated()
    {
        AddTarget(isActive: false);
        _store.GrantDirect(TargetUserId, AccessPermissionKeys.GrantsManage);

        Assert.False(await Guard().CallerCoversAsync(TargetSubject, CancellationToken.None));
    }

    [Fact]
    public async Task Only_a_global_grant_covers_a_global_grant()
    {
        AddTarget(isActive: true, organizationId: CallerOrganization);
        _store.GrantDirect(TargetUserId, AccessPermissionKeys.DashboardView);
        _store.GrantDirect(CallerUserId, AccessPermissionKeys.DashboardView, PermissionScope.OwnOrganization);

        Assert.False(await Guard().CallerCoversAsync(TargetSubject, CancellationToken.None));
    }

    [Fact]
    public async Task An_own_organization_grant_covers_one_in_the_same_organization_only()
    {
        AddTarget(isActive: true, organizationId: CallerOrganization);
        _store.GrantDirect(TargetUserId, AccessPermissionKeys.DashboardView, PermissionScope.OwnOrganization);
        _store.GrantDirect(CallerUserId, AccessPermissionKeys.DashboardView, PermissionScope.OwnOrganization);
        var sameOrganization = await Guard().CallerCoversAsync(TargetSubject, CancellationToken.None);
        AddTarget(isActive: true, organizationId: OtherOrganization);

        var otherOrganization = await Guard().CallerCoversAsync(TargetSubject, CancellationToken.None);

        Assert.True(sameOrganization);
        Assert.False(otherOrganization);
    }

    [Fact]
    public async Task Only_the_whole_catalog_covers_break_glass()
    {
        _directory.Add(TargetSubject, new DirectoryUser(UserId: TargetUserId, IsActive: true, HoldsAllPermissions: true));
        _store.GrantDirect(CallerUserId, AccessPermissionKeys.DashboardView);
        var partial = await Guard().CallerCoversAsync(TargetSubject, CancellationToken.None);
        foreach (var key in TestCatalogs.FullKeys.Where(key => key != AccessPermissionKeys.DashboardView))
        {
            _store.GrantDirect(CallerUserId, key);
        }

        var whole = await Guard().CallerCoversAsync(TargetSubject, CancellationToken.None);

        Assert.False(partial);
        Assert.True(whole);
    }

    [Fact]
    public async Task An_unknown_target_or_an_anonymous_caller_is_never_covered()
    {
        AddTarget(isActive: true);

        Assert.False(await Guard().CallerCoversAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.False(await Guard(new FakeCurrentUser(subjectId: null)).CallerCoversAsync(TargetSubject, CancellationToken.None));
    }

    [Fact]
    public async Task The_last_manager_check_uses_the_continuity_guard()
    {
        _accounts.Add(TargetUserId);
        _store.GrantDirect(TargetUserId, AccessPermissionKeys.RolesManage);
        _store.GrantDirect(TargetUserId, AccessPermissionKeys.GrantsManage);

        Assert.True(await Guard().IsLastAccessManagerAsync(TargetUserId, CancellationToken.None));
        Assert.False(await Guard().IsLastAccessManagerAsync(TargetUserId + 1, CancellationToken.None));
    }

    private void AddTarget(bool isActive, long? organizationId = null) =>
        _directory.Add(TargetSubject, new DirectoryUser(UserId: TargetUserId, IsActive: isActive, HoldsAllPermissions: false, OrganizationId: organizationId));

    private AdministrationGuard Guard() => Guard(new FakeCurrentUser(CallerSubject));

    private AdministrationGuard Guard(FakeCurrentUser currentUser) => new(
        currentUser,
        new UserPermissionResolver(_directory, _store, TestCatalogs.Full()),
        new ManagerContinuityGuard(_store, _accounts));
}

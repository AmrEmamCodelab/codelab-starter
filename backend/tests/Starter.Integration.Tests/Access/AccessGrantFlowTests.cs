using System.Net;
using System.Text.Json;
using Starter.Integration.Tests.Support;
using Starter.Tests.Hosting;
using static Starter.Integration.Tests.Access.AccessApi;

namespace Starter.Integration.Tests.Access;

/// <summary>
/// The role and grant editor end to end on a real database (Development host, own database): a role grant and a
/// virtual-role permission are live on the user's next request WITHOUT relogin, a revocation answers 403 (not 401) on
/// the next request, and the self-lockout guard refuses removing the last manager. Each test first resets the
/// Development user to its seeded grant (dashboard.view) and empties the virtual roles, so order does not matter.
/// </summary>
[Collection(AccessHostGroup.Name)]
public sealed class AccessGrantFlowTests(AccessHost host) : IAsyncLifetime, IDisposable
{
    private const string DashboardView = "dashboard.view";
    private const string RolesView = "access.roles.view";
    private const string RolesManage = "access.roles.manage";
    private const string GrantsView = "access.grants.view";
    private const string GrantsManage = "access.grants.manage";
    private const string LastManagerCode = "ACCESS_LAST_MANAGER";
    private const string OnlyBreakGlassWarning = "ACCESS_ONLY_BREAK_GLASS_MANAGES";
    private const string VirtualRoleNotAssignableCode = "ACCESS_VIRTUAL_ROLE_NOT_ASSIGNABLE";
    private const int AllInternalUsers = 1;
    private const int VirtualRoleSource = 3;

    private readonly AccessApi _api = new(host.Factory.CreateClient());
    private string _admin = null!;
    private SessionTokens _dev = null!;

    private string DevId => _dev.UserId.GetString()!;

    public async Task InitializeAsync()
    {
        _admin = (await _api.Session.LoginAsync(host.Accounts.BreakGlassUserName, host.Accounts.BreakGlassPassword)).AccessToken;
        _dev = await _api.Session.LoginAsync(host.Accounts.DevUserName, host.Accounts.DevUserPassword);
        using var reset = await _api.PutGrantsAsync(_admin, DevId, [], [PermissionEntry(DashboardView)], confirmBreakGlassOnly: true);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        await SetVirtualRolePermissionsAsync(AllInternalUsers, []);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task A_granted_role_is_live_on_the_next_call_and_its_revocation_answers_403_without_relogin()
    {
        using (var before = await _api.SendAsync(HttpMethod.Get, Paths.Roles, _dev.AccessToken))
        {
            await StarterProblems.AssertProblemAsync(before, HttpStatusCode.Forbidden);
        }

        var role = await _api.ExpectJsonAsync(HttpMethod.Post, Paths.Roles, _admin, HttpStatusCode.Created, new
        {
            code = "role-viewers",
            nameEn = "Role viewers",
            nameAr = "مشاهدو الأدوار",
            permissions = new[] { RolesView },
        });
        var roleId = role.GetProperty("id").GetString()!;
        using (var grant = await _api.PutGrantsAsync(_admin, DevId, [RoleEntry(roleId)], [PermissionEntry(DashboardView)]))
        {
            Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        }

        Assert.Contains(RolesView, await _api.MyPermissionsAsync(_dev.AccessToken));
        await _api.ExpectJsonAsync(HttpMethod.Get, Paths.Roles, _dev.AccessToken, HttpStatusCode.OK);

        using (var revoke = await _api.PutGrantsAsync(_admin, DevId, [], [PermissionEntry(DashboardView)]))
        {
            Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        }

        using var after = await _api.SendAsync(HttpMethod.Get, Paths.Roles, _dev.AccessToken);
        await StarterProblems.AssertProblemAsync(after, HttpStatusCode.Forbidden);
        Assert.Equal([DashboardView], await _api.MyPermissionsAsync(_dev.AccessToken));
    }

    [Fact]
    public async Task Deactivating_a_granted_role_removes_its_permissions_on_the_next_call()
    {
        var role = await _api.ExpectJsonAsync(HttpMethod.Post, Paths.Roles, _admin, HttpStatusCode.Created, new
        {
            code = "grant-viewers",
            nameEn = "Grant viewers",
            nameAr = "مشاهدو الصلاحيات",
            permissions = new[] { GrantsView },
        });
        var roleId = role.GetProperty("id").GetString()!;
        using (var grant = await _api.PutGrantsAsync(_admin, DevId, [RoleEntry(roleId)], [PermissionEntry(DashboardView)]))
        {
            Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        }

        Assert.Contains(GrantsView, await _api.MyPermissionsAsync(_dev.AccessToken));
        await _api.ExpectJsonAsync(HttpMethod.Put, Paths.Role(roleId), _admin, HttpStatusCode.OK, new
        {
            nameEn = "Grant viewers",
            nameAr = "مشاهدو الصلاحيات",
            permissions = new[] { GrantsView },
            isActive = false,
        });

        Assert.DoesNotContain(GrantsView, await _api.MyPermissionsAsync(_dev.AccessToken));
    }

    [Fact]
    public async Task A_virtual_role_permission_reaches_every_user_of_that_kind_and_is_never_assignable()
    {
        var internalRole = await SetVirtualRolePermissionsAsync(AllInternalUsers, [GrantsView]);

        Assert.Contains(GrantsView, await _api.MyPermissionsAsync(_dev.AccessToken));
        await _api.ExpectJsonAsync(HttpMethod.Get, Paths.Users, _dev.AccessToken, HttpStatusCode.OK);
        var grants = await _api.ExpectJsonAsync(HttpMethod.Get, Paths.Grants(DevId), _admin, HttpStatusCode.OK);
        var effective = grants.GetProperty("effective").EnumerateArray().Single(entry => entry.GetProperty("key").GetString() == GrantsView);
        Assert.Equal(VirtualRoleSource, effective.GetProperty("sources")[0].GetProperty("kind").GetInt32());
        Assert.Equal(internalRole, grants.GetProperty("virtualRoles").EnumerateArray().Single(entry => entry.GetProperty("virtualKind").GetInt32() == AllInternalUsers).GetProperty("roleId").GetString());

        using var assign = await _api.PutGrantsAsync(_admin, DevId, [RoleEntry(internalRole)], [PermissionEntry(DashboardView)]);
        Assert.Equal(VirtualRoleNotAssignableCode, await StarterProblems.AssertProblemAsync(assign, HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task The_self_lockout_guard_refuses_removing_the_last_manager_unless_break_glass_only_is_confirmed()
    {
        using (var promote = await _api.PutGrantsAsync(_admin, DevId, [], [PermissionEntry(DashboardView), PermissionEntry(RolesManage), PermissionEntry(GrantsManage)]))
        {
            Assert.Equal(HttpStatusCode.OK, promote.StatusCode);
        }

        using (var refused = await _api.PutGrantsAsync(_admin, DevId, [], [PermissionEntry(DashboardView)]))
        {
            Assert.Equal(LastManagerCode, await StarterProblems.AssertProblemAsync(refused, HttpStatusCode.Conflict));
        }

        Assert.Contains(RolesManage, await _api.MyPermissionsAsync(_dev.AccessToken));
        using var confirmed = await _api.PutGrantsAsync(_admin, DevId, [], [PermissionEntry(DashboardView)], confirmBreakGlassOnly: true);
        var body = await StarterClient.ReadJsonAsync(confirmed);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(OnlyBreakGlassWarning, body.GetProperty("warnings")[0].GetString());
        Assert.DoesNotContain(RolesManage, await _api.MyPermissionsAsync(_dev.AccessToken));
    }

    [Fact]
    public async Task The_catalog_and_the_three_virtual_roles_seeded_by_migration_are_listed()
    {
        var permissions = await _api.ExpectJsonAsync(HttpMethod.Get, Paths.Permissions, _admin, HttpStatusCode.OK);
        var roles = await _api.ExpectJsonAsync(HttpMethod.Get, Paths.Roles, _admin, HttpStatusCode.OK);

        Assert.Contains(permissions.EnumerateArray(), entry => entry.GetProperty("key").GetString() == GrantsManage && !entry.GetProperty("isRetired").GetBoolean());
        Assert.Equal([1, 2, 3], roles.EnumerateArray().Where(entry => entry.GetProperty("isVirtual").GetBoolean()).Select(entry => entry.GetProperty("virtualKind").GetInt32()));
        var picker = await _api.ExpectJsonAsync(HttpMethod.Get, Paths.Users + "?search=" + Uri.EscapeDataString(host.Accounts.DevUserName), _admin, HttpStatusCode.OK);
        Assert.Contains(picker.EnumerateArray(), entry => entry.GetProperty("id").GetString() == DevId);
    }

    private async Task<string> SetVirtualRolePermissionsAsync(int virtualKind, string[] permissions)
    {
        var roles = await _api.ExpectJsonAsync(HttpMethod.Get, Paths.Roles, _admin, HttpStatusCode.OK);
        var role = roles.EnumerateArray().Single(entry => entry.GetProperty("virtualKind").ValueKind == JsonValueKind.Number && entry.GetProperty("virtualKind").GetInt32() == virtualKind);
        var roleId = role.GetProperty("id").GetString()!;
        await _api.ExpectJsonAsync(HttpMethod.Put, Paths.Role(roleId), _admin, HttpStatusCode.OK, new
        {
            nameEn = role.GetProperty("nameEn").GetString(),
            nameAr = role.GetProperty("nameAr").GetString(),
            permissions,
            isActive = true,
            confirmBreakGlassOnly = true,
        });
        return roleId;
    }
}

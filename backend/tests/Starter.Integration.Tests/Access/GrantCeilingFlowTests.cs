using System.Net;
using Starter.Integration.Tests.Support;
using Starter.Integration.Tests.Users;
using Starter.Tests.Hosting;
using static Starter.Integration.Tests.Access.AccessApi;

namespace Starter.Integration.Tests.Access;

/// <summary>
/// The grant ceiling through the real host: a user holding <c>access.grants.manage</c> at own-organization scope gets
/// 403 <c>ACCESS_SCOPE_EXCEEDS_CALLER</c> when granting global scope, to themselves or to a colleague of their
/// organization, and can still grant at own-organization scope.
/// </summary>
[Collection(UsersHostGroup.Name)]
public sealed class GrantCeilingFlowTests(UsersHost host)
{
    private const string GrantsManage = "access.grants.manage";
    private const string DashboardView = "dashboard.view";
    private const string ScopeExceedsCallerCode = "ACCESS_SCOPE_EXCEEDS_CALLER";
    private const string Id = "id";

    [Fact]
    public async Task An_own_organization_grants_manager_gets_403_granting_global_scope_to_themselves_or_a_colleague()
    {
        using var client = host.CreateClient();
        using var users = new UsersApi(host.Factory.CreateClient());
        using var access = new AccessApi(host.Factory.CreateClient());
        var admin = (await client.LoginAsync(host.Accounts.BreakGlassUserName, host.Accounts.BreakGlassPassword)).AccessToken;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var organization = await users.CreateExternalOrganizationAsync("GCL-" + suffix, admin);
        var manager = await CreateUserAsync(users, "ceiling.manager." + suffix, organization, admin);
        var colleague = await CreateUserAsync(users, "ceiling.colleague." + suffix, organization, admin);
        var role = await access.ExpectJsonAsync(HttpMethod.Post, Paths.Roles, admin, HttpStatusCode.Created, new
        {
            code = "ceiling-" + suffix,
            nameEn = "Ceiling " + suffix,
            nameAr = "سقف " + suffix,
            permissions = new[] { DashboardView },
        });
        var roleId = role.GetProperty(Id).GetString()!;
        using (var granted = await access.PutGrantsAsync(admin, manager.Id, [], [PermissionEntry(GrantsManage, Scopes.OwnOrganization)]))
        {
            Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        }

        var token = await SignInAsync(client, users, manager, "ceiling.manager." + suffix);

        using var selfWiden = await access.PutGrantsAsync(token, manager.Id, [], [PermissionEntry(GrantsManage, Scopes.Global)]);
        using var colleagueKey = await access.PutGrantsAsync(token, colleague.Id, [], [PermissionEntry(GrantsManage, Scopes.Global)]);
        using var colleagueRole = await access.PutGrantsAsync(token, colleague.Id, [RoleEntry(roleId, Scopes.Global)], []);
        using var ownScope = await access.PutGrantsAsync(token, colleague.Id, [RoleEntry(roleId, Scopes.OwnOrganization)], [PermissionEntry(GrantsManage, Scopes.OwnOrganization)]);

        Assert.Equal(ScopeExceedsCallerCode, await StarterProblems.AssertProblemAsync(selfWiden, HttpStatusCode.Forbidden));
        Assert.Equal(ScopeExceedsCallerCode, await StarterProblems.AssertProblemAsync(colleagueKey, HttpStatusCode.Forbidden));
        Assert.Equal(ScopeExceedsCallerCode, await StarterProblems.AssertProblemAsync(colleagueRole, HttpStatusCode.Forbidden));
        Assert.Equal(HttpStatusCode.OK, ownScope.StatusCode);
        var managerGrants = await access.ExpectJsonAsync(HttpMethod.Get, Paths.Grants(manager.Id), admin, HttpStatusCode.OK);
        Assert.Equal(Scopes.OwnOrganization, managerGrants.GetProperty("permissions")[0].GetProperty("scope").GetInt32());
    }

    private static async Task<string> SignInAsync(StarterClient client, UsersApi users, (string Id, string Link) account, string userName)
    {
        var password = "Aa1!" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        using (var activated = await users.PostJsonAsync(UsersApi.Activate, new { ticket = UsersApi.TicketOf(account.Link), password }, accessToken: null))
        {
            Assert.Equal(HttpStatusCode.NoContent, activated.StatusCode);
        }

        return (await client.LoginAsync(userName, password)).AccessToken;
    }

    private static async Task<(string Id, string Link)> CreateUserAsync(UsersApi users, string userName, string organizationId, string adminToken)
    {
        using var created = await users.PostJsonAsync(
            UsersApi.Users,
            new { userName, displayName = "Ceiling User", email = userName + "@example.test", kind = UsersApi.ExternalKind, binding = UsersApi.PasswordBinding, organizationId },
            adminToken);
        var body = await UsersApi.ReadAsync(created, HttpStatusCode.Created);
        return (body.GetProperty("user").GetProperty(Id).GetString()!, body.GetProperty("activation").GetProperty("activationLink").GetString()!);
    }
}

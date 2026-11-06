using System.Net;
using Starter.Integration.Tests.Access;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests.Users;

/// <summary>
/// Organization scope through the real host: a user granted <c>users.view</c> at own-organization scope lists only
/// their organization's accounts and gets 404 for an account of another organization.
/// </summary>
[Collection(UsersHostGroup.Name)]
public sealed class UsersScopeFlowTests(UsersHost host)
{
    private const string UsersView = "users.view";
    private const string Items = "items";
    private const string Id = "id";

    [Fact]
    public async Task An_own_organization_holder_sees_only_their_organizations_users()
    {
        using var client = host.CreateClient();
        using var api = new UsersApi(host.Factory.CreateClient());
        using var access = new AccessApi(host.Factory.CreateClient());
        var admin = await client.LoginAsync(host.Accounts.BreakGlassUserName, host.Accounts.BreakGlassPassword);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var own = await api.CreateExternalOrganizationAsync("OWN-" + suffix, admin.AccessToken);
        var other = await api.CreateExternalOrganizationAsync("OTH-" + suffix, admin.AccessToken);
        var viewer = await CreateUserAsync(api, "scope.viewer." + suffix, own, admin.AccessToken);
        var colleague = await CreateUserAsync(api, "scope.colleague." + suffix, own, admin.AccessToken);
        var stranger = await CreateUserAsync(api, "scope.stranger." + suffix, other, admin.AccessToken);
        using (var granted = await access.PutGrantsAsync(admin.AccessToken, viewer.Id, [], [AccessApi.PermissionEntry(UsersView, AccessApi.Scopes.OwnOrganization)]))
        {
            Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        }

        var password = "Aa1!" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        using (var activated = await api.PostJsonAsync(UsersApi.Activate, new { ticket = UsersApi.TicketOf(viewer.Link), password }, accessToken: null))
        {
            Assert.Equal(HttpStatusCode.NoContent, activated.StatusCode);
        }

        var session = await client.LoginAsync("scope.viewer." + suffix, password);

        using var list = await api.GetAsync(UsersApi.Users + "?search=scope.&pageSize=100", session.AccessToken);
        var listed = (await UsersApi.ReadAsync(list, HttpStatusCode.OK)).GetProperty(Items).EnumerateArray().Select(user => user.GetProperty(Id).GetString()).ToList();
        using var foreign = await api.GetAsync(UsersApi.UserPath(stranger.Id), session.AccessToken);
        using var foreignFilter = await api.GetAsync(UsersApi.Users + "?organizationId=" + other, session.AccessToken);

        Assert.Contains(colleague.Id, listed);
        Assert.Contains(viewer.Id, listed);
        Assert.DoesNotContain(stranger.Id, listed);
        await StarterProblems.AssertProblemAsync(foreign, HttpStatusCode.NotFound);
        Assert.Empty((await UsersApi.ReadAsync(foreignFilter, HttpStatusCode.OK)).GetProperty(Items).EnumerateArray());
    }

    private static async Task<(string Id, string Link)> CreateUserAsync(UsersApi api, string userName, string organizationId, string adminToken)
    {
        using var created = await api.PostJsonAsync(
            UsersApi.Users,
            new { userName, displayName = "Scope User", email = (string?)null, kind = UsersApi.ExternalKind, binding = UsersApi.PasswordBinding, organizationId },
            adminToken);
        var body = await UsersApi.ReadAsync(created, HttpStatusCode.Created);
        return (body.GetProperty("user").GetProperty(Id).GetString()!, body.GetProperty("activation").GetProperty("activationLink").GetString()!);
    }
}

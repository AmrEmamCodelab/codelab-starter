using System.Net;
using System.Text.Json;
using Starter.Integration.Tests.Support;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests;

/// <summary>
/// The phase 2 session flow end to end on a real database (Development host): break-glass login, the dashboard,
/// the Development user's 403, logout revoking the access token, and refresh-token rotation.
/// </summary>
[Collection(DevelopmentHostGroup.Name)]
public sealed class SessionFlowTests(DevelopmentHost host)
{
    private const string DashboardView = "dashboard.view";
    private const string AccessAdminView = "access.admin.view";
    private const string InvalidCredentialsCode = "IDENTITY_INVALID_CREDENTIALS";
    private const string RefreshRejectedCode = "IDENTITY_REFRESH_REJECTED";

    [Fact]
    public async Task BreakGlassLogsInAndOpensTheDashboard()
    {
        using var client = host.CreateClient();
        var session = await client.LoginAsync(host.Accounts.BreakGlassUserName, host.Accounts.BreakGlassPassword);

        using var dashboard = await client.GetAsync(StarterClient.Paths.Dashboard, session.AccessToken);

        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Equal(JsonValueKind.String, session.UserId.ValueKind); // a PublicId, never the numeric database id
        Assert.True(session.Body.GetProperty("idleTimeoutMinutes").GetInt32() > 0);
    }

    [Fact]
    public async Task BreakGlassHoldsEveryCatalogPermission()
    {
        using var client = host.CreateClient();
        var session = await client.LoginAsync(host.Accounts.BreakGlassUserName, host.Accounts.BreakGlassPassword);

        var permissions = await PermissionsAsync(client, session.AccessToken);

        Assert.Contains(DashboardView, permissions);
        Assert.Contains(AccessAdminView, permissions);
    }

    [Fact]
    public async Task TheDevUserGetsTheDashboardButAccessAdmin403ProblemJson()
    {
        using var client = host.CreateClient();
        var session = await client.LoginAsync(host.Accounts.DevUserName, host.Accounts.DevUserPassword);

        using var dashboard = await client.GetAsync(StarterClient.Paths.Dashboard, session.AccessToken);
        using var admin = await client.GetAsync(StarterClient.Paths.AccessAdmin, session.AccessToken);

        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        await StarterProblems.AssertProblemAsync(admin, HttpStatusCode.Forbidden);
        Assert.Equal([DashboardView], await PermissionsAsync(client, session.AccessToken));
    }

    [Fact]
    public async Task LogoutAnswers204AndTheReplayedAccessTokenGets401()
    {
        using var client = host.CreateClient();
        var session = await client.LoginAsync(host.Accounts.BreakGlassUserName, host.Accounts.BreakGlassPassword);

        using var logout = await client.SendAsync(HttpMethod.Post, StarterClient.Paths.Logout, session.AccessToken);
        using var replay = await client.GetAsync(StarterClient.Paths.MyPermissions, session.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        await StarterProblems.AssertProblemAsync(replay, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshRotatesTheTokenAndRefusesTheOldOne()
    {
        using var client = host.CreateClient();
        var first = await client.LoginAsync(host.Accounts.BreakGlassUserName, host.Accounts.BreakGlassPassword);

        using var rotated = await client.PostRefreshAsync(first.RefreshToken);
        var second = await StarterClient.ReadSessionAsync(rotated);
        using var reused = await client.PostRefreshAsync(first.RefreshToken);

        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.Equal(RefreshRejectedCode, await StarterProblems.AssertProblemAsync(reused, HttpStatusCode.BadRequest));
        using var afterTheft = await client.PostRefreshAsync(second.RefreshToken);
        Assert.Equal(RefreshRejectedCode, await StarterProblems.AssertProblemAsync(afterTheft, HttpStatusCode.BadRequest)); // reuse ends the session
    }

    [Fact]
    public async Task AWrongPasswordIsARefusalProblemJson()
    {
        using var client = host.CreateClient();

        using var response = await client.PostLoginAsync(host.Accounts.BreakGlassUserName, host.Accounts.DevUserPassword);

        Assert.Equal(InvalidCredentialsCode, await StarterProblems.AssertProblemAsync(response, HttpStatusCode.BadRequest));
    }

    private static async Task<string[]> PermissionsAsync(StarterClient client, string accessToken)
    {
        using var response = await client.GetAsync(StarterClient.Paths.MyPermissions, accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await StarterClient.ReadJsonAsync(response);
        return [.. body.GetProperty("permissions").EnumerateArray().Select(key => key.GetString()!)];
    }
}

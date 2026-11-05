using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Starter.Domain.Users;
using Starter.Integration.Tests.LoginModes.Support;
using Starter.Integration.Tests.Support;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Application.LoginModes;

namespace Starter.Integration.Tests.LoginModes;

/// <summary>
/// The three login modes end to end on SQL Server: Active Directory login against the in-process LDAP double, and the
/// full SSO code flow against the dev IdP hosted in process (login, authorize with login_hint, callback, fragment
/// ticket, exchange, an authenticated call, SSO logout, end_session accepted by the IdP with the hint, old token 401).
/// </summary>
[Collection(LoginModesHostGroup.Name)]
public sealed class LoginModeFlowTests(LoginModesHost host)
{
    private const string ModesPath = "/api/auth/modes";
    private const string TabBindingProof = "a-browser-binding-secret-of-at-least-32-characters";
    private const string DirectoryLoginPath = "/api/auth/ad/login";
    private const string SsoLoginPath = "/api/auth/sso/login";
    private const string SsoExchangePath = "/api/auth/sso/exchange";
    private const string SsoLogoutPath = "/api/auth/sso/logout";
    private const string BasicLoginPath = "/api/auth/login";
    private const string SsoUser = "dev.admin";
    private const string UnmatchedSsoUser = "dev.unmatched";
    private const string LoginHint = "login_hint";
    private const string Bearer = "Bearer";

    [Fact]
    public async Task All_three_modes_are_offered()
    {
        using var api = host.CreateBrowser();

        var body = await api.GetFromJsonAsync<JsonElement>(ModesPath);

        Assert.True(body.GetProperty("basic").GetBoolean() && body.GetProperty("activeDirectory").GetBoolean() && body.GetProperty("sso").GetBoolean());
    }

    [Fact]
    public async Task A_directory_password_signs_a_directory_bound_account_in_and_nothing_else()
    {
        var loginName = "ad" + Guid.NewGuid().ToString("N")[..8];
        var secret = NewDirectorySecret();
        await host.CreateAccountAsync(loginName, AccountBinding.ActiveDirectory);
        host.Directory.Add(loginName + "@" + LoginModesHost.DirectoryDomain, secret, loginName);
        using var api = host.CreateBrowser();

        using var signedIn = await api.PostAsJsonAsync(DirectoryLoginPath, new Credentials(loginName, secret));
        var session = await StarterClient.ReadSessionAsync(signedIn);
        using var permissions = await SendAsync(api, HttpMethod.Get, StarterClient.Paths.MyPermissions, session.AccessToken);
        using var wrong = await api.PostAsJsonAsync(DirectoryLoginPath, new Credentials(loginName, secret + "x"));
        using var basic = await api.PostAsJsonAsync(BasicLoginPath, new Credentials(loginName, secret));

        Assert.Equal(HttpStatusCode.OK, permissions.StatusCode);
        await AssertCodeAsync(wrong, HttpStatusCode.BadRequest, IdentityErrorCodes.InvalidCredentials);
        await AssertCodeAsync(basic, HttpStatusCode.BadRequest, IdentityErrorCodes.InvalidCredentials);
        Assert.Equal(2, Convert.ToInt32(await TestSqlServer.ScalarAsync(host.DatabaseName,
            "SELECT TOP 1 SignInMode FROM [identity].[UserSessions] ORDER BY Id DESC"), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task A_disabled_or_unknown_directory_account_and_an_unreachable_directory_are_refused()
    {
        var loginName = "ad" + Guid.NewGuid().ToString("N")[..8];
        var secret = NewDirectorySecret();
        await host.CreateAccountAsync(loginName, AccountBinding.ActiveDirectory);
        host.Directory.Add(loginName + "@" + LoginModesHost.DirectoryDomain, secret, loginName, disabled: true);
        using var api = host.CreateBrowser();

        using var disabled = await api.PostAsJsonAsync(DirectoryLoginPath, new Credentials(loginName, secret));
        host.Directory.IsDown = true;
        try
        {
            using var down = await api.PostAsJsonAsync(DirectoryLoginPath, new Credentials(loginName, secret));
            await AssertCodeAsync(down, HttpStatusCode.ServiceUnavailable, LoginModeErrorCodes.DirectoryUnavailable);
        }
        finally
        {
            host.Directory.IsDown = false;
        }

        await AssertCodeAsync(disabled, HttpStatusCode.BadRequest, IdentityErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task The_sso_code_flow_signs_in_and_out_through_the_identity_provider()
    {
        await host.EnsureAccountAsync(SsoUser, AccountBinding.Sso);
        using var api = host.CreateBrowser();

        var fragment = await SignInThroughProviderAsync(api, SsoUser, returnUrl: "/admin");
        Assert.Equal("/admin", fragment.GetValueOrDefault(SsoSpaFragment.ReturnUrlKey));
        using var forwarded = await api.PostAsJsonAsync(SsoExchangePath, new { ticket = fragment[SsoSpaFragment.TicketKey] });
        await AssertCodeAsync(forwarded, HttpStatusCode.BadRequest, LoginModeErrorCodes.SsoTicketRejected);
        fragment = await SignInThroughProviderAsync(api, SsoUser, returnUrl: "/admin");
        using var exchanged = await api.PostAsJsonAsync(SsoExchangePath, new { ticket = fragment[SsoSpaFragment.TicketKey], bindingProof = TabBindingProof });
        var session = await StarterClient.ReadSessionAsync(exchanged);
        using var replay = await api.PostAsJsonAsync(SsoExchangePath, new { ticket = fragment[SsoSpaFragment.TicketKey], bindingProof = TabBindingProof });
        using var permissions = await SendAsync(api, HttpMethod.Get, StarterClient.Paths.MyPermissions, session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, permissions.StatusCode);
        await AssertCodeAsync(replay, HttpStatusCode.BadRequest, LoginModeErrorCodes.SsoTicketRejected);
        Assert.Equal(3, Convert.ToInt32(await TestSqlServer.ScalarAsync(host.DatabaseName,
            "SELECT TOP 1 SignInMode FROM [identity].[UserSessions] WHERE IdTokenHint IS NOT NULL ORDER BY Id DESC"), System.Globalization.CultureInfo.InvariantCulture));

        using var logout = await SendAsync(api, HttpMethod.Post, SsoLogoutPath, session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        var endSessionUrl = new Uri((await StarterClient.ReadJsonAsync(logout)).GetProperty("endSessionUrl").GetString()!);
        Assert.Contains("id_token_hint=", endSessionUrl.Query, StringComparison.Ordinal);
        using var browser = host.IdentityProvider.CreateClient();
        using var endSession = await browser.GetAsync(endSessionUrl);
        Assert.Equal(HttpStatusCode.Redirect, endSession.StatusCode);
        Assert.StartsWith(LoginModesHost.PostLogoutRedirectUri, endSession.Headers.Location!.AbsoluteUri, StringComparison.Ordinal);

        using var afterLogout = await SendAsync(api, HttpMethod.Get, StarterClient.Paths.MyPermissions, session.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task An_sso_identity_without_a_matching_account_comes_back_as_an_error_fragment()
    {
        using var api = host.CreateBrowser();

        var fragment = await SignInThroughProviderAsync(api, UnmatchedSsoUser, returnUrl: "/");

        Assert.Equal(IdentityErrorCodes.InvalidCredentials, fragment[SsoSpaFragment.ErrorKey]);
        Assert.False(fragment.ContainsKey(SsoSpaFragment.TicketKey));
    }

    [Fact]
    public async Task Sso_logout_of_a_basic_session_ends_it_without_an_end_session_url()
    {
        using var api = host.CreateBrowser();
        using var client = new StarterClient(host.Factory.CreateClient());
        var breakGlass = await client.LoginAsync(userName: host.Accounts.BreakGlassUserName, password: host.Accounts.BreakGlassPassword);

        using var logout = await SendAsync(api, HttpMethod.Post, SsoLogoutPath, breakGlass.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var afterLogout = await SendAsync(api, HttpMethod.Get, StarterClient.Paths.MyPermissions, breakGlass.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    /// <summary>Plays the browser: API sso/login, the IdP's authorize with a login_hint, then the API callback; returns the SPA fragment.</summary>
    private async Task<Dictionary<string, string>> SignInThroughProviderAsync(HttpClient api, string loginHint, string returnUrl)
    {
        using var login = await api.GetAsync(SsoLoginPath + "?returnUrl=" + Uri.EscapeDataString(returnUrl) + "&binding=" + SsoBinding.HashOf(TabBindingProof));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var authorize = login.Headers.Location!;

        using var browser = host.IdentityProvider.CreateClient();
        using var authorized = await browser.GetAsync(new Uri(authorize.AbsoluteUri + "&" + LoginHint + "=" + Uri.EscapeDataString(loginHint)));
        Assert.Equal(HttpStatusCode.Redirect, authorized.StatusCode);
        var callback = authorized.Headers.Location!;
        Assert.StartsWith(LoginModesHost.RedirectUri, callback.AbsoluteUri, StringComparison.Ordinal);

        using var completed = await api.GetAsync(callback.PathAndQuery);
        Assert.Equal(HttpStatusCode.Redirect, completed.StatusCode);
        var spa = completed.Headers.Location!;
        Assert.StartsWith(LoginModesHost.WebBaseUrl + SsoSpaFragment.CallbackPath + "#", spa.AbsoluteUri, StringComparison.Ordinal);
        return spa.Fragment.TrimStart('#').Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);
    }

    private static string NewDirectorySecret() => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));

    private static async Task<HttpResponseMessage> SendAsync(HttpClient api, HttpMethod method, string path, string accessToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(Bearer, accessToken);
        return await api.SendAsync(request);
    }

    private static async Task AssertCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await StarterClient.ReadJsonAsync(response);
        Assert.Equal((status, code), (response.StatusCode, body.GetProperty("code").GetString()));
    }
}

/// <summary>A user name and password body, as the login endpoints bind it.</summary>
/// <param name="UserName">The user name.</param>
/// <param name="Password">The password.</param>
internal sealed record Credentials(string UserName, string Password);

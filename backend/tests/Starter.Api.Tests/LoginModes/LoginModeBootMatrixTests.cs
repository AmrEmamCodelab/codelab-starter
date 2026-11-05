using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Starter.Api.Tests.Support;
using Starter.Modules.Identity.Api.LoginModes;
using Starter.Modules.Identity.Application.LoginModes;
using Starter.Modules.Identity.Infrastructure.LoginModes;

namespace Starter.Api.Tests.LoginModes;

/// <summary>
/// The Auth:Modes boot matrix (phase 3 exit criterion): every coherent mode list boots and <c>GET /api/auth/modes</c>
/// reports exactly it; a disabled mode's endpoints answer 404 <c>IDENTITY_LOGIN_MODE_DISABLED</c>; incoherent
/// configuration fails the start.
/// </summary>
[Collection(HostTestGroup.Name)]
public sealed class LoginModeBootMatrixTests
{
    private const string ModesPath = "/api/auth/modes";
    private const string BasicLoginPath = "/api/auth/login";
    private const string DirectoryLoginPath = "/api/auth/ad/login";
    private const string SsoLoginPath = "/api/auth/sso/login";
    private const string SsoExchangePath = "/api/auth/sso/exchange";
    private const string ProblemJson = "application/problem+json";
    private const string TabBindingProof = "a-browser-binding-secret-of-at-least-32-characters";

    public static TheoryData<string[], bool, bool, bool> CoherentModes => new()
    {
        { [AuthModeNames.Basic], true, false, false },
        { [AuthModeNames.ActiveDirectory], false, true, false },
        { [AuthModeNames.Sso], false, false, true },
        { [AuthModeNames.Basic, AuthModeNames.ActiveDirectory, AuthModeNames.Sso], true, true, true },
    };

    [Theory]
    [MemberData(nameof(CoherentModes))]
    public async Task Each_coherent_mode_list_boots_and_reports_exactly_its_modes(string[] modes, bool basic, bool directory, bool sso)
    {
        await using var app = await LoginModeHost.StartAsync(
            LoginModeHost.Merge(LoginModeHost.Modes(modes), LoginModeHost.DirectorySection(), LoginModeHost.SsoSection()));
        using var client = app.GetTestClient();

        var body = await client.GetFromJsonAsync<JsonElement>(ModesPath);

        Assert.Equal((basic, directory, sso), (body.GetProperty("basic").GetBoolean(), body.GetProperty("activeDirectory").GetBoolean(), body.GetProperty("sso").GetBoolean()));
        await AssertGateAsync(client, BasicLoginPath, open: basic);
        await AssertGateAsync(client, DirectoryLoginPath, open: directory);
        await AssertGateAsync(client, SsoExchangePath, open: sso);
    }

    [Fact]
    public async Task Without_auth_modes_only_basic_login_is_offered()
    {
        await using var app = await LoginModeHost.StartAsync(new Dictionary<string, string?>());
        using var client = app.GetTestClient();

        var body = await client.GetFromJsonAsync<JsonElement>(ModesPath);

        Assert.True(body.GetProperty("basic").GetBoolean());
        Assert.False(body.GetProperty("sso").GetBoolean());
        using var ssoLogin = await client.GetAsync(SsoLoginPath);
        await AssertDisabledAsync(ssoLogin);
    }

    [Fact]
    public async Task Sso_login_redirects_to_the_provider_with_pkce_and_sets_the_sealed_sign_in_cookie()
    {
        await using var app = await LoginModeHost.StartAsync(LoginModeHost.Merge(LoginModeHost.Modes(AuthModeNames.Sso), LoginModeHost.SsoSection()));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(SsoLoginPath + "?returnUrl=%2Fadmin&binding=" + SsoBinding.HashOf(TabBindingProof));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith(LoginModeHost.IdentityProvider + "/authorize?", location.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", location.Query, StringComparison.Ordinal);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith(SsoPendingCookie.NamePrefix, cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth/sso", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sso_login_without_a_browser_binding_is_refused()
    {
        await using var app = await LoginModeHost.StartAsync(LoginModeHost.Merge(LoginModeHost.Modes(AuthModeNames.Sso), LoginModeHost.SsoSection()));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(SsoLoginPath + "?returnUrl=%2Fadmin");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task A_callback_without_its_cookie_redirects_to_the_spa_with_an_error_fragment()
    {
        await using var app = await LoginModeHost.StartAsync(LoginModeHost.Merge(LoginModeHost.Modes(AuthModeNames.Sso), LoginModeHost.SsoSection()));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/api/auth/sso/signin-oidc?code=c&state=s");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(LoginModeHost.WebBaseUrl + SsoSpaFragment.CallbackPath + "#error=" + LoginModeErrorCodes.SsoRejected, response.Headers.Location!.AbsoluteUri);
    }

    public static TheoryData<string, Dictionary<string, string?>> IncoherentConfigurations => new()
    {
        { "Codelab:Identity:Sso:Authority", Without(LoginModeHost.Merge(LoginModeHost.Modes(AuthModeNames.Sso), LoginModeHost.SsoSection()), "Codelab:Identity:Sso:Authority") },
        { "Codelab:Identity:ActiveDirectory:Host", LoginModeHost.Modes(AuthModeNames.ActiveDirectory) },
        { "Auth:Modes", LoginModeHost.Modes("kerberos") },
        { "Auth:Modes", LoginModeHost.Modes(AuthModeNames.Basic, AuthModeNames.Basic) },
        { "Auth:Modes", new Dictionary<string, string?> { ["Auth:Modes"] = string.Empty } },
        { "Auth:WebBaseUrl", Without(LoginModeHost.Merge(LoginModeHost.Modes(AuthModeNames.Sso), LoginModeHost.SsoSection()), "Auth:WebBaseUrl") },
        { "Codelab:Security:Tickets:Enabled", Without(LoginModeHost.Merge(LoginModeHost.Modes(AuthModeNames.Sso), LoginModeHost.SsoSection()), "Codelab:Security:Tickets:Enabled") },
    };

    [Theory]
    [MemberData(nameof(IncoherentConfigurations))]
    public async Task Incoherent_configuration_fails_the_start(string namedKey, Dictionary<string, string?> settings)
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(() => LoginModeHost.StartAsync(settings));

        Assert.Contains(namedKey, Describe(error), StringComparison.Ordinal);
    }

    private static async Task AssertGateAsync(HttpClient client, string path, bool open)
    {
        using var response = await client.PostAsJsonAsync(path, new { });
        if (open)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        else
        {
            await AssertDisabledAsync(response);
        }
    }

    private static async Task AssertDisabledAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(LoginModeErrorCodes.LoginModeDisabled, body.GetProperty("code").GetString());
    }

    private static Dictionary<string, string?> Without(Dictionary<string, string?> settings, string key)
    {
        settings.Remove(key);
        return settings;
    }

    private static string Describe(Exception error) =>
        error is AggregateException aggregate ? string.Join(" | ", aggregate.InnerExceptions.Select(Describe)) : error.Message + " " + error.InnerException?.Message;
}

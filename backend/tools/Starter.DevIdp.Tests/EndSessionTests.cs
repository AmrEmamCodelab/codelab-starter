using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Starter.DevIdp.Protocol;
using Starter.DevIdp.Tests.Support;

namespace Starter.DevIdp.Tests;

public sealed class EndSessionTests
{
    [Fact]
    public async Task A_valid_hint_redirects_to_a_registered_post_logout_uri_with_the_state()
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var hint = await IdTokenAsync(idp);

        using var response = await EndSessionAsync(idp, hint, DevIdpTestServer.PostLogoutRedirectUri, state: "bye");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(DevIdpTestServer.PostLogoutRedirectUri + "?state=bye", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task An_expired_hint_is_still_accepted()
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var hint = await IdTokenAsync(idp);

        idp.Clock.Advance(TimeSpan.FromHours(1));
        using var response = await EndSessionAsync(idp, hint, DevIdpTestServer.PostLogoutRedirectUri);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example.test/signed-out")]
    [InlineData("http://localhost:4280/login")]
    public async Task An_unregistered_post_logout_uri_shows_the_signed_out_page_without_redirecting(string postLogoutRedirectUri)
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var hint = await IdTokenAsync(idp);

        using var response = await EndSessionAsync(idp, hint, postLogoutRedirectUri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Contains("signed out", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_hint_shows_the_signed_out_page_and_never_redirects()
    {
        await using var idp = await DevIdpTestServer.StartAsync();

        using var response = await EndSessionAsync(idp, hint: null, DevIdpTestServer.PostLogoutRedirectUri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task A_tampered_hint_is_refused()
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var hint = await IdTokenAsync(idp);
        var tampered = hint[..^4] + (hint.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

        using var response = await EndSessionAsync(idp, tampered, DevIdpTestServer.PostLogoutRedirectUri);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task A_hint_signed_by_another_run_is_refused()
    {
        await using var other = await DevIdpTestServer.StartAsync();
        var foreignHint = await IdTokenAsync(other);
        await using var idp = await DevIdpTestServer.StartAsync();

        using var response = await EndSessionAsync(idp, foreignHint, DevIdpTestServer.PostLogoutRedirectUri);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_client_id_that_differs_from_the_hint_audience_is_refused()
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var hint = await IdTokenAsync(idp);
        var query = QueryHelpers.AddQueryString(OidcRoutes.EndSession, new Dictionary<string, string?>
        {
            [OidcParameterNames.IdTokenHint] = hint,
            [OidcParameterNames.ClientId] = DevIdpTestServer.PublicClientId,
            [OidcParameterNames.PostLogoutRedirectUri] = DevIdpTestServer.PostLogoutRedirectUri,
        });

        using var response = await idp.Browser.GetAsync(query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<string> IdTokenAsync(DevIdpTestServer idp) =>
        (await idp.SignInAsync()).GetProperty("id_token").GetString()!;

    private static Task<HttpResponseMessage> EndSessionAsync(DevIdpTestServer idp, string? hint, string postLogoutRedirectUri, string? state = null)
    {
        var parameters = new Dictionary<string, string?>
        {
            [OidcParameterNames.IdTokenHint] = hint,
            [OidcParameterNames.PostLogoutRedirectUri] = postLogoutRedirectUri,
            [OidcParameterNames.State] = state,
        };
        return idp.Browser.GetAsync(QueryHelpers.AddQueryString(OidcRoutes.EndSession, parameters.Where(pair => pair.Value is not null)));
    }
}

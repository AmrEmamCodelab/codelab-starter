using System.Net;
using Starter.DevIdp.Protocol;
using Starter.DevIdp.Tests.Support;

namespace Starter.DevIdp.Tests;

public sealed class AuthorizeTests
{
    [Fact]
    public async Task A_login_hint_signs_in_at_once_and_redirects_with_a_code_and_the_state()
    {
        await using var idp = await DevIdpTestServer.StartAsync();

        using var response = await idp.AuthorizeAsync(DevIdpTestServer.AuthorizeParameters(DevIdpTestServer.NewVerifier()));
        var query = DevIdpTestServer.RedirectQuery(response);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(DevIdpTestServer.RedirectUri + "?", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(query["code"]));
        Assert.Equal("state-123", query["state"]);
    }

    [Theory]
    [InlineData("dev-sso-0002")]
    [InlineData("DEV.ADMIN")]
    [InlineData("dev.admin@example.test")]
    public async Task A_login_hint_matches_the_subject_login_name_email_or_upn(string hint)
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var verifier = DevIdpTestServer.NewVerifier();

        var code = await idp.CodeAsync(verifier, parameters => parameters[OidcParameterNames.LoginHint] = hint);
        using var response = await idp.TokenAsync(code, verifier);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_login_hint_the_picker_lists_every_fixture_user_and_carries_the_request()
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var parameters = DevIdpTestServer.AuthorizeParameters(DevIdpTestServer.NewVerifier(), loginHint: null);
        parameters[OidcParameterNames.State] = "<script>";

        using var response = await idp.AuthorizeAsync(parameters);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<main>", html, StringComparison.Ordinal);
        Assert.Contains("value=\"dev-sso-0001\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"dev-sso-0003\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"code_challenge\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Posting_the_picker_form_signs_the_chosen_user_in()
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var form = DevIdpTestServer.AuthorizeParameters(DevIdpTestServer.NewVerifier(), loginHint: "dev-sso-0001")
            .ToDictionary(pair => pair.Key, pair => pair.Value!);

        using var response = await idp.Browser.PostAsync(OidcRoutes.Authorize, new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(string.IsNullOrEmpty(DevIdpTestServer.RedirectQuery(response)["code"]));
    }

    [Theory]
    [InlineData(OidcParameterNames.ClientId, "unknown-client")]
    [InlineData(OidcParameterNames.ClientId, null)]
    [InlineData(OidcParameterNames.RedirectUri, "https://evil.example.test/callback")]
    [InlineData(OidcParameterNames.RedirectUri, "https://rp.example.test/signin-oidc/")]
    [InlineData(OidcParameterNames.RedirectUri, null)]
    public async Task An_untrusted_client_or_redirect_uri_is_refused_on_an_idp_page_and_never_redirected(string name, string? value)
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var parameters = DevIdpTestServer.AuthorizeParameters(DevIdpTestServer.NewVerifier());
        parameters[name] = value;

        using var response = await idp.AuthorizeAsync(parameters);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData(OidcParameterNames.ResponseType, "token", "unsupported_response_type")]
    [InlineData(OidcParameterNames.ResponseType, null, "unsupported_response_type")]
    [InlineData(OidcParameterNames.Scope, "profile email", "invalid_scope")]
    [InlineData(OidcParameterNames.Scope, null, "invalid_scope")]
    [InlineData(OidcParameterNames.CodeChallenge, null, "invalid_request")]
    [InlineData(OidcParameterNames.CodeChallenge, "too-short", "invalid_request")]
    [InlineData(OidcParameterNames.CodeChallengeMethod, "plain", "invalid_request")]
    [InlineData(OidcParameterNames.CodeChallengeMethod, null, "invalid_request")]
    [InlineData(OidcParameterNames.LoginHint, "nobody@example.test", "access_denied")]
    public async Task A_protocol_error_is_redirected_to_the_client_with_the_state(string name, string? value, string error)
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var parameters = DevIdpTestServer.AuthorizeParameters(DevIdpTestServer.NewVerifier());
        parameters[name] = value;

        using var response = await idp.AuthorizeAsync(parameters);
        var query = DevIdpTestServer.RedirectQuery(response);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(error, query["error"]);
        Assert.Equal("state-123", query["state"]);
        Assert.False(query.ContainsKey("code"));
    }

    [Fact]
    public async Task A_repeated_parameter_counts_as_missing()
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var query = "?client_id=test-confidential&client_id=test-confidential&redirect_uri=" + Uri.EscapeDataString(DevIdpTestServer.RedirectUri);

        using var response = await idp.Browser.GetAsync(OidcRoutes.Authorize + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_post_that_is_not_a_form_is_refused()
    {
        await using var idp = await DevIdpTestServer.StartAsync();

        using var response = await idp.Browser.PostAsync(OidcRoutes.Authorize, new StringContent("{}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

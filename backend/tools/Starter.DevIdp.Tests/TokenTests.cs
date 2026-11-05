using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Starter.DevIdp.Protocol;
using Starter.DevIdp.Tests.Support;

namespace Starter.DevIdp.Tests;

public sealed class TokenTests
{
    [Fact]
    public async Task The_id_token_validates_against_the_published_jwks_and_carries_the_identity_and_the_nonce()
    {
        await using var idp = await DevIdpTestServer.StartAsync();

        var tokens = await idp.SignInAsync();
        var result = await ValidateAsync(idp, tokens.GetProperty("id_token").GetString()!);
        var token = (JsonWebToken)result.SecurityToken;

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal("dev-sso-0001", token.Subject);
        Assert.Equal("nonce-456", token.GetClaim("nonce").Value);
        Assert.Equal("dev.viewer@example.test", token.GetClaim("email").Value);
        Assert.Equal("dev.viewer", token.GetClaim("preferred_username").Value);
        Assert.Equal("dev.viewer@example.test", token.GetClaim("upn").Value);
        Assert.Equal("Dev Viewer", token.GetClaim("name").Value);
        Assert.Equal(DevIdpTestServer.ConfidentialClientId, token.GetClaim("azp").Value);
        Assert.Equal(idp.Clock.GetUtcNow().ToUnixTimeSeconds(), long.Parse(token.GetClaim("iat").Value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(TimeSpan.FromSeconds(300), token.ValidTo - token.IssuedAt);
    }

    [Fact]
    public async Task The_token_response_is_bearer_and_never_cached()
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var verifier = DevIdpTestServer.NewVerifier();

        using var response = await idp.TokenAsync(await idp.CodeAsync(verifier), verifier);
        var body = await DevIdpTestServer.ReadJsonAsync(response);

        Assert.Equal("Bearer", body.GetProperty("token_type").GetString());
        Assert.Equal(300, body.GetProperty("expires_in").GetInt32());
        Assert.Equal(DevIdpTestServer.Scope, body.GetProperty("scope").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("access_token").GetString()));
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_nonce_the_id_token_has_none()
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var verifier = DevIdpTestServer.NewVerifier();

        var code = await idp.CodeAsync(verifier, parameters => parameters[OidcParameterNames.Nonce] = null);
        using var response = await idp.TokenAsync(code, verifier);
        var token = new JsonWebToken((await DevIdpTestServer.ReadJsonAsync(response)).GetProperty("id_token").GetString());

        Assert.False(token.TryGetClaim("nonce", out _));
    }

    [Fact]
    public async Task A_wrong_pkce_verifier_is_invalid_grant()
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var code = await idp.CodeAsync(DevIdpTestServer.NewVerifier());

        using var response = await idp.TokenAsync(code, DevIdpTestServer.NewVerifier());

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "invalid_grant");
    }

    [Fact]
    public async Task A_code_works_once_and_a_failed_attempt_also_consumes_it()
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var verifier = DevIdpTestServer.NewVerifier();
        var code = await idp.CodeAsync(verifier);
        var otherCode = await idp.CodeAsync(verifier);

        using var first = await idp.TokenAsync(code, verifier);
        using var reuse = await idp.TokenAsync(code, verifier);
        using var failed = await idp.TokenAsync(otherCode, DevIdpTestServer.NewVerifier());
        using var afterFailure = await idp.TokenAsync(otherCode, verifier);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await AssertErrorAsync(reuse, HttpStatusCode.BadRequest, "invalid_grant");
        await AssertErrorAsync(failed, HttpStatusCode.BadRequest, "invalid_grant");
        await AssertErrorAsync(afterFailure, HttpStatusCode.BadRequest, "invalid_grant");
    }

    [Fact]
    public async Task A_code_expires_after_its_lifetime()
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var verifier = DevIdpTestServer.NewVerifier();
        var code = await idp.CodeAsync(verifier);

        idp.Clock.Advance(TimeSpan.FromSeconds(60));
        using var response = await idp.TokenAsync(code, verifier);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "invalid_grant");
    }

    [Fact]
    public async Task A_code_redeemed_with_another_redirect_uri_or_client_is_invalid_grant()
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var verifier = DevIdpTestServer.NewVerifier();

        using var otherRedirect = await idp.TokenAsync(await idp.CodeAsync(verifier), verifier, form => form[OidcParameterNames.RedirectUri] = "https://rp.example.test/other");
        using var otherClient = await idp.TokenAsync(await idp.CodeAsync(verifier), verifier, PublicClient);

        await AssertErrorAsync(otherRedirect, HttpStatusCode.BadRequest, "invalid_grant");
        await AssertErrorAsync(otherClient, HttpStatusCode.BadRequest, "invalid_grant");
    }

    [Theory]
    [InlineData("wrong-secret")]
    [InlineData(null)]
    public async Task A_confidential_client_without_its_exact_secret_is_invalid_client(string? secret)
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var verifier = DevIdpTestServer.NewVerifier();
        var code = await idp.CodeAsync(verifier);

        using var response = await idp.TokenAsync(code, verifier, form => SetOrRemove(form, OidcParameterNames.ClientSecret, secret));

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, "invalid_client");
    }

    [Fact]
    public async Task An_unknown_client_is_invalid_client()
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var verifier = DevIdpTestServer.NewVerifier();

        using var response = await idp.TokenAsync(await idp.CodeAsync(verifier), verifier, form => form[OidcParameterNames.ClientId] = "unknown");

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, "invalid_client");
    }

    [Fact]
    public async Task Client_secret_basic_authenticates_the_client()
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var verifier = DevIdpTestServer.NewVerifier();
        var code = await idp.CodeAsync(verifier);
        var form = new Dictionary<string, string>
        {
            [OidcParameterNames.GrantType] = OidcValues.GrantTypeAuthorizationCode,
            [OidcParameterNames.Code] = code,
            [OidcParameterNames.RedirectUri] = DevIdpTestServer.RedirectUri,
            [OidcParameterNames.CodeVerifier] = verifier,
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, OidcRoutes.Token) { Content = new FormUrlEncodedContent(form) };
        var credentials = Encoding.UTF8.GetBytes($"{DevIdpTestServer.ConfidentialClientId}:{Uri.EscapeDataString(idp.ClientSecret)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(credentials));

        using var response = await idp.Browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_public_client_authenticates_by_pkce_alone()
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var verifier = DevIdpTestServer.NewVerifier();
        var code = await idp.CodeAsync(verifier, parameters => parameters[OidcParameterNames.ClientId] = DevIdpTestServer.PublicClientId);

        using var response = await idp.TokenAsync(code, verifier, PublicClient);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Another_grant_type_is_unsupported()
    {
        await using var idp = await DevIdpTestServer.StartAsync();

        using var response = await idp.TokenAsync("x", DevIdpTestServer.NewVerifier(), form => form[OidcParameterNames.GrantType] = "client_credentials");

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "unsupported_grant_type");
    }

    [Fact]
    public async Task A_token_request_that_is_not_a_form_is_invalid_request()
    {
        await using var idp = await DevIdpTestServer.StartAsync();

        using var response = await idp.Browser.PostAsync(OidcRoutes.Token, new StringContent("{}", Encoding.UTF8, "application/json"));

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "invalid_request");
    }

    internal static async Task<TokenValidationResult> ValidateAsync(DevIdpTestServer idp, string idToken)
    {
        using var jwksResponse = await idp.Browser.GetAsync(OidcRoutes.Jwks);
        var jwks = new JsonWebKeySet(await jwksResponse.Content.ReadAsStringAsync());
        return await new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer = "http://localhost:5190",
            ValidAudience = DevIdpTestServer.ConfidentialClientId,
            IssuerSigningKeys = jwks.GetSigningKeys(),
            ValidAlgorithms = ["RS256"],
        });
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string error)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(error, (await DevIdpTestServer.ReadJsonAsync(response)).GetProperty("error").GetString());
    }

    private static void PublicClient(Dictionary<string, string> form)
    {
        form[OidcParameterNames.ClientId] = DevIdpTestServer.PublicClientId;
        form.Remove(OidcParameterNames.ClientSecret);
    }

    private static void SetOrRemove(Dictionary<string, string> form, string name, string? value)
    {
        if (value is null)
        {
            form.Remove(name);
        }
        else
        {
            form[name] = value;
        }
    }
}

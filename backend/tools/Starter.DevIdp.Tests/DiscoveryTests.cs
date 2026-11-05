using System.Net;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Starter.DevIdp.Protocol;
using Starter.DevIdp.Tests.Support;

namespace Starter.DevIdp.Tests;

public sealed class DiscoveryTests
{
    [Fact]
    public async Task Discovery_publishes_every_endpoint_on_the_issuer_and_only_the_supported_values()
    {
        await using var idp = await DevIdpTestServer.StartAsync();

        using var response = await idp.Browser.GetAsync(OidcRoutes.Discovery);
        var document = await DevIdpTestServer.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("http://localhost:5190", document.GetProperty("issuer").GetString());
        Assert.Equal("http://localhost:5190/authorize", document.GetProperty("authorization_endpoint").GetString());
        Assert.Equal("http://localhost:5190/token", document.GetProperty("token_endpoint").GetString());
        Assert.Equal("http://localhost:5190/jwks", document.GetProperty("jwks_uri").GetString());
        Assert.Equal("http://localhost:5190/userinfo", document.GetProperty("userinfo_endpoint").GetString());
        Assert.Equal("http://localhost:5190/end-session", document.GetProperty("end_session_endpoint").GetString());
        Assert.Equal("code", Assert.Single(Strings(document, "response_types_supported")));
        Assert.Equal("authorization_code", Assert.Single(Strings(document, "grant_types_supported")));
        Assert.Equal("S256", Assert.Single(Strings(document, "code_challenge_methods_supported")));
        Assert.Equal("RS256", Assert.Single(Strings(document, "id_token_signing_alg_values_supported")));
        Assert.Contains("openid", Strings(document, "scopes_supported"));
        Assert.Contains("upn", Strings(document, "claims_supported"));
    }

    [Fact]
    public async Task Discovery_follows_a_configured_issuer()
    {
        await using var idp = await DevIdpTestServer.StartAsync(options => options.Issuer = "https://idp.example.test/");

        using var response = await idp.Browser.GetAsync(OidcRoutes.Discovery);
        var document = await DevIdpTestServer.ReadJsonAsync(response);

        Assert.Equal("https://idp.example.test/", document.GetProperty("issuer").GetString());
        Assert.Equal("https://idp.example.test/token", document.GetProperty("token_endpoint").GetString());
    }

    [Fact]
    public async Task Jwks_publishes_one_public_rsa_key_whose_kid_signs_the_id_tokens()
    {
        await using var idp = await DevIdpTestServer.StartAsync();

        using var response = await idp.Browser.GetAsync(OidcRoutes.Jwks);
        var key = Assert.Single((await DevIdpTestServer.ReadJsonAsync(response)).GetProperty("keys").EnumerateArray());
        var tokens = await idp.SignInAsync();
        var idToken = new JsonWebToken(tokens.GetProperty("id_token").GetString());

        Assert.Equal("RSA", key.GetProperty("kty").GetString());
        Assert.Equal("sig", key.GetProperty("use").GetString());
        Assert.Equal("RS256", key.GetProperty("alg").GetString());
        Assert.False(string.IsNullOrEmpty(key.GetProperty("n").GetString()));
        Assert.Equal("AQAB", key.GetProperty("e").GetString());
        Assert.False(key.TryGetProperty("d", out _), "the private exponent must never be published");
        Assert.Equal(key.GetProperty("kid").GetString(), idToken.Kid);
        Assert.Equal("RS256", idToken.Alg);
    }

    [Fact]
    public async Task Every_start_generates_a_new_signing_key()
    {
        await using var first = await DevIdpTestServer.StartAsync();
        await using var second = await DevIdpTestServer.StartAsync();

        Assert.NotEqual(await KidAsync(first), await KidAsync(second));
    }

    private static async Task<string?> KidAsync(DevIdpTestServer idp)
    {
        using var response = await idp.Browser.GetAsync(OidcRoutes.Jwks);
        return (await DevIdpTestServer.ReadJsonAsync(response)).GetProperty("keys")[0].GetProperty("kid").GetString();
    }

    private static string[] Strings(JsonElement document, string name) =>
        document.GetProperty(name).EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
}

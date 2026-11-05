using System.Net;
using System.Net.Http.Headers;
using Starter.DevIdp.Protocol;
using Starter.DevIdp.Tests.Support;

namespace Starter.DevIdp.Tests;

public sealed class UserInfoTests
{
    [Fact]
    public async Task A_live_access_token_returns_the_user_claims()
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var tokens = await idp.SignInAsync();

        using var response = await SendAsync(idp, tokens.GetProperty("access_token").GetString());
        var claims = await DevIdpTestServer.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("dev-sso-0001", claims.GetProperty("sub").GetString());
        Assert.Equal("dev.viewer@example.test", claims.GetProperty("email").GetString());
        Assert.Equal("dev.viewer", claims.GetProperty("preferred_username").GetString());
        Assert.Equal("dev.viewer@example.test", claims.GetProperty("upn").GetString());
        Assert.Equal("Dev Viewer", claims.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-token")]
    public async Task A_missing_or_unknown_token_is_401_invalid_token(string? accessToken)
    {
        await using var idp = await DevIdpTestServer.StartAsync();

        using var response = await SendAsync(idp, accessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid_token", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_access_token_expires_after_the_token_lifetime()
    {
        await using var idp = await DevIdpTestServer.StartAsync();
        var tokens = await idp.SignInAsync();

        idp.Clock.Advance(TimeSpan.FromSeconds(300));
        using var response = await SendAsync(idp, tokens.GetProperty("access_token").GetString());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static Task<HttpResponseMessage> SendAsync(DevIdpTestServer idp, string? accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, OidcRoutes.UserInfo);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(OidcValues.TokenTypeBearer, accessToken);
        }

        return idp.Browser.SendAsync(request);
    }
}

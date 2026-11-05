using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Starter.Integration.Tests.Support;

/// <summary>The tokens of a signed-in session, read from the login or refresh body.</summary>
/// <param name="AccessToken">The bearer access token.</param>
/// <param name="RefreshToken">The single-use refresh token.</param>
/// <param name="UserId">The user's PublicId, as sent.</param>
/// <param name="Body">The whole response body.</param>
public sealed record SessionTokens(string AccessToken, string RefreshToken, JsonElement UserId, JsonElement Body);

/// <summary>A thin HTTP client over the host's public API (paths as the contract states them; no module types).</summary>
public sealed class StarterClient(HttpClient http) : IDisposable
{
    /// <summary>The API paths under test (contract section 5).</summary>
    public static class Paths
    {
        public const string Login = "/api/auth/login";
        public const string Refresh = "/api/auth/refresh";
        public const string Logout = "/api/auth/logout";
        public const string MyPermissions = "/api/me/permissions";
        public const string Features = "/api/features";
        public const string Dashboard = "/api/dashboard";
        public const string AccessAdmin = "/api/access/admin";
    }

    private const string BearerScheme = "Bearer";

    /// <summary>Signs in; the response is returned as is.</summary>
    public Task<HttpResponseMessage> PostLoginAsync(string userName, string password) =>
        http.PostAsJsonAsync(Paths.Login, new { userName, password });

    /// <summary>Signs in and returns the session (fails the test unless the answer is 200).</summary>
    public async Task<SessionTokens> LoginAsync(string userName, string password)
    {
        using var response = await PostLoginAsync(userName, password);
        return await ReadSessionAsync(response);
    }

    /// <summary>Rotates <paramref name="refreshToken"/>; the response is returned as is.</summary>
    public Task<HttpResponseMessage> PostRefreshAsync(string refreshToken) =>
        http.PostAsJsonAsync(Paths.Refresh, new { refreshToken });

    /// <summary>Sends <paramref name="method"/> <paramref name="path"/> with the access token, or anonymously when it is null.</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? accessToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, accessToken);
        }

        return await http.SendAsync(request);
    }

    /// <summary>GET with the access token.</summary>
    public Task<HttpResponseMessage> GetAsync(string path, string? accessToken) => SendAsync(HttpMethod.Get, path, accessToken);

    /// <summary>Reads a 200 session body.</summary>
    public static async Task<SessionTokens> ReadSessionAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected 200, got {(int)response.StatusCode}: {text}");
        var body = JsonDocument.Parse(text).RootElement.Clone();
        return new SessionTokens(
            AccessToken: body.GetProperty("accessToken").GetString()!,
            RefreshToken: body.GetProperty("refreshToken").GetString()!,
            UserId: body.GetProperty("user").GetProperty("id"),
            Body: body);
    }

    /// <summary>Reads a JSON body.</summary>
    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    /// <inheritdoc />
    public void Dispose() => http.Dispose();
}

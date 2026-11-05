using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Starter.Integration.Tests.Support;

namespace Starter.Integration.Tests.Access;

/// <summary>
/// The Development host on its OWN throwaway database, so the Access tests can change the Development user's grants
/// and the virtual roles without touching the state other test classes assert on.
/// </summary>
public sealed class AccessHost() : IntegrationHost(Environments.Development);

/// <summary>Tests sharing the Access host.</summary>
[CollectionDefinition(Name)]
public sealed class AccessHostGroup : ICollectionFixture<AccessHost>
{
    /// <summary>The collection name.</summary>
    public const string Name = "access host";
}

/// <summary>
/// A thin JSON client for the role and grant editor (paths and member names as the contract states them; no module
/// types, so the tests read the wire like the frontend does).
/// </summary>
public sealed class AccessApi(HttpClient http) : IDisposable
{
    /// <summary>The editor paths under test (contract section 5, Access rows).</summary>
    public static class Paths
    {
        public const string Permissions = "/api/admin/access/permissions";
        public const string Roles = "/api/admin/access/roles";
        public const string Users = "/api/admin/access/users";

        public static string Role(string roleId) => Roles + "/" + roleId;

        public static string Grants(string userId) => Users + "/" + userId + "/grants";
    }

    /// <summary>The scope values on the wire.</summary>
    public static class Scopes
    {
        public const int Global = 1;
        public const int OwnOrganization = 2;
    }

    private const string BearerScheme = "Bearer";

    /// <summary>The underlying session client (login, refresh, my permissions).</summary>
    public StarterClient Session { get; } = new(http);

    /// <summary>Sends <paramref name="method"/> <paramref name="path"/> with an optional JSON body.</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string accessToken, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await http.SendAsync(request);
    }

    /// <summary>Sends and reads a JSON body, failing the test unless the status is <paramref name="expected"/>.</summary>
    public async Task<JsonElement> ExpectJsonAsync(HttpMethod method, string path, string accessToken, HttpStatusCode expected, object? body = null)
    {
        using var response = await SendAsync(method, path, accessToken, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{method} {path}: expected {(int)expected}, got {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>The caller's permission keys now.</summary>
    public async Task<IReadOnlyList<string>> MyPermissionsAsync(string accessToken)
    {
        using var response = await Session.GetAsync(StarterClient.Paths.MyPermissions, accessToken);
        var body = await StarterClient.ReadJsonAsync(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. body.GetProperty("permissions").EnumerateArray().Select(key => key.GetString()!)];
    }

    /// <summary>Replaces a user's roles and direct grants and returns the response.</summary>
    public Task<HttpResponseMessage> PutGrantsAsync(string adminToken, string userId, object[] roles, object[] permissions, bool confirmBreakGlassOnly = false) =>
        SendAsync(HttpMethod.Put, Paths.Grants(userId), adminToken, new { roles, permissions, confirmBreakGlassOnly });

    /// <summary>A role entry of a grant write.</summary>
    public static object RoleEntry(string roleId, int scope = Scopes.Global) => new { roleId, scope };

    /// <summary>A permission entry of a grant write.</summary>
    public static object PermissionEntry(string key, int scope = Scopes.Global) => new { key, scope };

    /// <inheritdoc />
    public void Dispose() => Session.Dispose();
}

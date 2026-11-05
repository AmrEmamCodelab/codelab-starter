using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using Starter.DevIdp.Configuration;
using Starter.DevIdp.Protocol;

namespace Starter.DevIdp.Tests.Support;

/// <summary>
/// A started <see cref="DevIdpInProcessServer"/> on a fake clock with two extra clients: a confidential one with a
/// secret generated per run and a public one. Helpers play the browser and the relying party.
/// </summary>
internal sealed class DevIdpTestServer : IAsyncDisposable
{
    public const string ConfidentialClientId = "test-confidential";
    public const string PublicClientId = "test-public";
    public const string RedirectUri = "https://rp.example.test/signin-oidc";
    public const string PostLogoutRedirectUri = "https://rp.example.test/signed-out";
    public const string Scope = "openid profile email";
    public const string ViewerHint = "dev.viewer";

    private DevIdpTestServer(DevIdpInProcessServer server, FakeTimeProvider clock, string clientSecret)
    {
        Server = server;
        Clock = clock;
        ClientSecret = clientSecret;
        Browser = server.CreateClient();
    }

    public DevIdpInProcessServer Server { get; }

    public FakeTimeProvider Clock { get; }

    public string ClientSecret { get; }

    public HttpClient Browser { get; }

    public static async Task<DevIdpTestServer> StartAsync(Action<DevIdpOptions>? configure = null)
    {
        var clock = new FakeTimeProvider(TimeProvider.System.GetUtcNow());
        var secret = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var server = await DevIdpInProcessServer.StartAsync(
            options =>
            {
                options.Clients.Add(Client(ConfidentialClientId, secret));
                options.Clients.Add(Client(PublicClientId, secret: null));
                configure?.Invoke(options);
            },
            clock);
        return new DevIdpTestServer(server, clock, secret);
    }

    public static string NewVerifier() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    /// <summary>The authorize query for the confidential client, with overrides (a null value removes the parameter).</summary>
    public static Dictionary<string, string?> AuthorizeParameters(string verifier, string? loginHint = ViewerHint) => new()
    {
        [OidcParameterNames.ClientId] = ConfidentialClientId,
        [OidcParameterNames.RedirectUri] = RedirectUri,
        [OidcParameterNames.ResponseType] = OidcValues.ResponseTypeCode,
        [OidcParameterNames.Scope] = Scope,
        [OidcParameterNames.State] = "state-123",
        [OidcParameterNames.Nonce] = "nonce-456",
        [OidcParameterNames.CodeChallenge] = Pkce.ChallengeOf(verifier),
        [OidcParameterNames.CodeChallengeMethod] = OidcValues.CodeChallengeMethodS256,
        [OidcParameterNames.LoginHint] = loginHint,
    };

    public Task<HttpResponseMessage> AuthorizeAsync(IDictionary<string, string?> parameters) =>
        Browser.GetAsync(QueryHelpers.AddQueryString(OidcRoutes.Authorize, parameters.Where(pair => pair.Value is not null)));

    /// <summary>Signs the viewer in non-interactively and returns the code from the redirect.</summary>
    public async Task<string> CodeAsync(string verifier, Action<Dictionary<string, string?>>? adjust = null)
    {
        var parameters = AuthorizeParameters(verifier);
        adjust?.Invoke(parameters);
        using var response = await AuthorizeAsync(parameters);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return RedirectQuery(response)[OidcParameterNames.Code].ToString();
    }

    public Task<HttpResponseMessage> TokenAsync(string code, string verifier, Action<Dictionary<string, string>>? adjust = null)
    {
        var form = new Dictionary<string, string>
        {
            [OidcParameterNames.GrantType] = OidcValues.GrantTypeAuthorizationCode,
            [OidcParameterNames.Code] = code,
            [OidcParameterNames.RedirectUri] = RedirectUri,
            [OidcParameterNames.CodeVerifier] = verifier,
            [OidcParameterNames.ClientId] = ConfidentialClientId,
            [OidcParameterNames.ClientSecret] = ClientSecret,
        };
        adjust?.Invoke(form);
        return Browser.PostAsync(OidcRoutes.Token, new FormUrlEncodedContent(form));
    }

    /// <summary>The full code flow for the viewer; returns the token response JSON.</summary>
    public async Task<JsonElement> SignInAsync()
    {
        var verifier = NewVerifier();
        var code = await CodeAsync(verifier);
        using var response = await TokenAsync(code, verifier);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    public static Dictionary<string, StringValues> RedirectQuery(HttpResponseMessage response) =>
        QueryHelpers.ParseQuery(response.Headers.Location!.Query);

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        Browser.Dispose();
        await Server.DisposeAsync();
    }

    private static DevIdpClient Client(string clientId, string? secret)
    {
        var client = new DevIdpClient { ClientId = clientId, ClientSecret = secret };
        client.RedirectUris.Add(RedirectUri);
        client.PostLogoutRedirectUris.Add(PostLogoutRedirectUri);
        return client;
    }
}

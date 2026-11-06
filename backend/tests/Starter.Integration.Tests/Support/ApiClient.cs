using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Starter.Integration.Tests.Support;

/// <summary>
/// A JSON client over the host's API for the phase 5 flows: optional bearer token and <c>Accept-Language</c>, paths as
/// the contract states them (no module types on the wire).
/// </summary>
public sealed class ApiClient(HttpClient http) : IDisposable
{
    private const string BearerScheme = "Bearer";

    /// <summary>GET <paramref name="path"/>.</summary>
    public Task<HttpResponseMessage> GetAsync(string path, string? accessToken, string? language = null) =>
        SendAsync(HttpMethod.Get, path, body: null, new CallOptions(accessToken, language));

    /// <summary>POST <paramref name="path"/> with a JSON <paramref name="body"/> (or none).</summary>
    public Task<HttpResponseMessage> PostAsync(string path, object? body, string? accessToken) =>
        SendAsync(HttpMethod.Post, path, body, new CallOptions(accessToken, Language: null));

    /// <summary>PUT <paramref name="path"/> with a JSON <paramref name="body"/>.</summary>
    public Task<HttpResponseMessage> PutAsync(string path, object body, string? accessToken) =>
        SendAsync(HttpMethod.Put, path, body, new CallOptions(accessToken, Language: null));

    /// <summary>The body of a response with <paramref name="expected"/> status (fails the test otherwise).</summary>
    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        ArgumentNullException.ThrowIfNull(response);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {text}");
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <inheritdoc />
    public void Dispose() => http.Dispose();

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CallOptions options)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (options.AccessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, options.AccessToken);
        }

        if (options.Language is not null)
        {
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(options.Language));
        }

        return await http.SendAsync(request);
    }

    private sealed record CallOptions(string? AccessToken, string? Language);
}

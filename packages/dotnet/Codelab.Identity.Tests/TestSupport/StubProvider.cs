using System.Net;
using System.Text;
using Codelab.Identity.Oidc;

namespace Codelab.Identity.Tests.TestSupport;

/// <summary>A stub HTTP handler routed by absolute URI; records every request (and the token request's form).</summary>
internal sealed class StubProvider : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> routes = new(StringComparer.Ordinal);

    public List<HttpRequestMessage> Requests { get; } = [];

    public Dictionary<string, string>? LastForm { get; private set; }

    public int CallsTo(string uri) => Requests.Count(request => request.RequestUri!.AbsoluteUri.StartsWith(uri, StringComparison.Ordinal));

    public StubProvider On(string uri, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        routes[uri] = respond;
        return this;
    }

    public StubProvider Json(string uri, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        On(uri, _ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    public StubProvider Throws(string uri, Exception failure) => On(uri, _ => throw failure);

    public static string TokenResponse(string? idToken) =>
        idToken is null ? "{\"access_token\":\"at\",\"token_type\":\"Bearer\"}" : $"{{\"access_token\":\"at\",\"token_type\":\"Bearer\",\"id_token\":\"{idToken}\"}}";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        if (request.Content is FormUrlEncodedContent form)
        {
            var text = await form.ReadAsStringAsync(cancellationToken);
            LastForm = text.Split('&').Select(pair => pair.Split('=')).ToDictionary(parts => Uri.UnescapeDataString(parts[0]), parts => Uri.UnescapeDataString(parts[1].Replace('+', ' ')));
        }

        var key = request.RequestUri!.GetLeftPart(UriPartial.Path);
        return routes.TryGetValue(key, out var respond) ? respond(request) : new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}

/// <summary>An <see cref="IHttpClientFactory"/> that hands out clients over one stub handler.</summary>
internal sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public string? LastName { get; private set; }

    public HttpClient CreateClient(string name)
    {
        LastName = name;
        return new HttpClient(handler, disposeHandler: false) { MaxResponseContentBufferSize = OidcHttpClient.MaxResponseBytes };
    }
}

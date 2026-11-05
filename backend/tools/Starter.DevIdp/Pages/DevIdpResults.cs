using System.Net.Mime;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Starter.DevIdp.Protocol;

namespace Starter.DevIdp.Pages;

/// <summary>The HTTP results the endpoints return: HTML pages, OAuth error JSON and redirects with query parameters.</summary>
internal static class DevIdpResults
{
    /// <summary>The Cache-Control value for every token, userinfo and error response.</summary>
    public const string NoStore = "no-store";

    /// <summary>An HTML page.</summary>
    public static IResult Html(string html, int statusCode = StatusCodes.Status200OK) =>
        Results.Content(html, MediaTypeNames.Text.Html, Encoding.UTF8, statusCode);

    /// <summary>An error the IdP shows on its own page (400).</summary>
    public static IResult ErrorPage(OidcError error) => Html(DevIdpPages.Error(error), StatusCodes.Status400BadRequest);

    /// <summary>An OAuth error as JSON (<c>{"error", "error_description"}</c>).</summary>
    public static IResult ErrorJson(OidcError error, int statusCode = StatusCodes.Status400BadRequest) =>
        Results.Json(error, statusCode: statusCode);

    /// <summary>A 302 to <paramref name="uri"/> with <paramref name="parameters"/> added to its query (null values are left out).</summary>
    public static IResult RedirectWith(string uri, params KeyValuePair<string, string?>[] parameters) =>
        Results.Redirect(QueryHelpers.AddQueryString(uri, parameters.Where(parameter => parameter.Value is not null)));

    /// <summary>A 302 carrying an OAuth error (and the state) back to the client.</summary>
    public static IResult RedirectError(string uri, OidcError error, string? state) =>
        RedirectWith(
            uri,
            new(OidcParameterNames.Error, error.Code),
            new(OidcParameterNames.ErrorDescription, error.Description),
            new(OidcParameterNames.State, state));

    /// <summary>Marks the response as never cacheable (token and userinfo responses carry credentials).</summary>
    public static void DisableCaching(HttpResponse response) => response.Headers.CacheControl = NoStore;
}

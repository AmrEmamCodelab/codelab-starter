using System.Text;
using System.Text.Encodings.Web;
using Starter.DevIdp.Authorize;
using Starter.DevIdp.Configuration;
using Starter.DevIdp.Protocol;

namespace Starter.DevIdp.Pages;

/// <summary>
/// The few HTML pages of the dev IdP: the fixture-user picker, an error page and the signed-out page. Plain semantic
/// HTML (a main landmark, a heading, a labelled form with one button per user) so it works with a keyboard and a
/// screen reader; every value is HTML-encoded. Developer-facing English only: this tool is never shown to end users.
/// </summary>
internal static class DevIdpPages
{
    private const string Style =
        "body{font-family:system-ui,sans-serif;margin:2rem;max-width:40rem}"
        + "button{display:block;margin-block:.5rem;padding:.5rem 1rem;inline-size:100%;text-align:start}";

    /// <summary>The picker: one submit button per fixture user, carrying the request back to POST /authorize.</summary>
    public static string Picker(AuthorizationRequest request, IEnumerable<DevIdpUser> users)
    {
        var body = new StringBuilder();
        body.Append("<h1>Dev IdP: choose a test user</h1>");
        body.Append("<p>Signing in to <strong>").Append(Encode(request.Client.ClientId)).Append("</strong>. Not a real identity provider.</p>");
        body.Append("<form method=\"post\" action=\"").Append(Encode(OidcRoutes.Authorize)).Append("\">");
        foreach (var field in request.ToFormFields())
        {
            body.Append("<input type=\"hidden\" name=\"").Append(Encode(field.Key)).Append("\" value=\"").Append(Encode(field.Value)).Append("\">");
        }

        body.Append("<fieldset><legend>Test users</legend>");
        foreach (var user in users)
        {
            body.Append("<button type=\"submit\" name=\"").Append(OidcParameterNames.LoginHint).Append("\" value=\"").Append(Encode(user.Subject)).Append("\">")
                .Append(Encode(user.DisplayName ?? user.Subject)).Append(" (").Append(Encode(user.Email ?? user.LoginName ?? user.Subject)).Append(")</button>");
        }

        body.Append("</fieldset></form>");
        return Document("Dev IdP sign-in", body.ToString());
    }

    /// <summary>An error the IdP shows itself instead of redirecting (an untrusted client or redirect URI, a bad hint).</summary>
    public static string Error(OidcError error) =>
        Document(
            "Dev IdP error",
            $"<h1>Sign-in request refused</h1><p role=\"alert\"><code>{Encode(error.Code)}</code>: {Encode(error.Description)}</p>");

    /// <summary>The signed-out page, with an optional note (for example why no redirect happened).</summary>
    public static string SignedOut(string? note)
    {
        var noteHtml = note is null ? string.Empty : $"<p>{Encode(note)}</p>";
        return Document("Dev IdP signed out", $"<h1>You are signed out of the dev IdP</h1>{noteHtml}");
    }

    private static string Document(string title, string body) =>
        $"<!doctype html><html lang=\"en\" dir=\"ltr\"><head><meta charset=\"utf-8\"><title>{Encode(title)}</title>"
        + $"<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><style>{Style}</style></head>"
        + $"<body><main>{body}</main></body></html>";

    private static string Encode(string value) => HtmlEncoder.Default.Encode(value);
}

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Starter.DevIdp.Configuration;
using Starter.DevIdp.Protocol;

namespace Starter.DevIdp.Token;

/// <summary>
/// Authenticates the client of a token request: client_secret_basic (an <c>Authorization: Basic</c> header with the
/// form-url-encoded id and secret) or client_secret_post (<c>client_id</c> + <c>client_secret</c> in the form). A
/// confidential client must present its exact secret (fixed-time compare); a public client (no secret configured)
/// authenticates by its <c>client_id</c> alone, PKCE then proves possession, and a presented secret is ignored.
/// </summary>
internal static class ClientAuthenticator
{
    private const char CredentialSeparator = ':';

    /// <summary>The authenticated client, or <see langword="null"/> (invalid_client).</summary>
    public static DevIdpClient? Authenticate(OidcParameters form, string? authorizationHeader, DevIdpOptions options)
    {
        var credentials = authorizationHeader is null
            ? new ClientCredentials(form.Get(OidcParameterNames.ClientId), form.Get(OidcParameterNames.ClientSecret))
            : ParseBasic(authorizationHeader);
        if (credentials is null || !SameClientInForm(credentials, form))
        {
            return null;
        }

        var client = options.FindClient(credentials.ClientId);
        if (client is null)
        {
            return null;
        }

        return !client.IsConfidential || SecretMatches(client.ClientSecret!, credentials.Secret) ? client : null;
    }

    private static bool SameClientInForm(ClientCredentials credentials, OidcParameters form)
    {
        var formClientId = form.Get(OidcParameterNames.ClientId);
        return formClientId is null || string.Equals(formClientId, credentials.ClientId, StringComparison.Ordinal);
    }

    private static ClientCredentials? ParseBasic(string authorizationHeader)
    {
        if (!AuthenticationHeaderValue.TryParse(authorizationHeader, out var header)
            || !string.Equals(header.Scheme, OidcValues.AuthenticationSchemeBasic, StringComparison.OrdinalIgnoreCase)
            || header.Parameter is null)
        {
            return null;
        }

        var buffer = new byte[header.Parameter.Length];
        if (!Convert.TryFromBase64String(header.Parameter, buffer, out var written))
        {
            return null;
        }

        var decoded = Encoding.UTF8.GetString(buffer, 0, written);
        var separator = decoded.IndexOf(CredentialSeparator, StringComparison.Ordinal);
        return separator < 0
            ? null
            : new ClientCredentials(WebUtility.UrlDecode(decoded[..separator]), WebUtility.UrlDecode(decoded[(separator + 1)..]));
    }

    private static bool SecretMatches(string expected, string? presented) =>
        presented is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(presented));

    private sealed record ClientCredentials(string? ClientId, string? Secret);
}

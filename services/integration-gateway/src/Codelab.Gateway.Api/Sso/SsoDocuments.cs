using System.Text.Json;

namespace Codelab.Gateway.Api.Sso;

/// <summary>
/// The shape checks the SSO profile runs before relaying an upstream answer: the gateway relays the provider's JSON
/// verbatim, but never an answer that is not the document the backend expects (an HTML error page, a captive portal).
/// </summary>
internal static class SsoDocuments
{
    /// <summary>The member of a JWKS document that holds the keys.</summary>
    public const string KeysMember = "keys";

    /// <summary>True when <paramref name="body"/> is a JSON object (a token response or a token error).</summary>
    public static bool IsJsonObject(byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return TryParse(body, document => document.RootElement.ValueKind == JsonValueKind.Object);
    }

    /// <summary>True when <paramref name="body"/> is a JSON object whose <see cref="KeysMember"/> is an array.</summary>
    public static bool IsJwks(byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return TryParse(body, document =>
            document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty(KeysMember, out var keys)
            && keys.ValueKind == JsonValueKind.Array);
    }

    private static bool TryParse(byte[] body, Func<JsonDocument, bool> check)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return check(document);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

using System.Text.Json;
using Codelab.Identity.Diagnostics;

namespace Codelab.Identity.Oidc;

/// <summary>What the token endpoint answered: the raw id_token, or the failure (then the token is null).</summary>
internal sealed record TokenExchange(string? IdToken, OidcSignInResult? Failure);

/// <summary>
/// The token request (RFC 6749 4.1.3 with the RFC 7636 verifier, <c>client_secret_post</c> when a secret is configured)
/// over the configured <see cref="IOidcBackChannel"/> (direct, or the integration gateway relay). Never throws for a
/// provider fault: 5xx and transport failures are unavailable, 4xx and an answer without an id_token are rejections. The
/// OAuth <c>error</c> code is logged (a fixed vocabulary); the body is not.
/// </summary>
internal sealed class OidcTokenClient(IOidcBackChannel backChannel, ILogger logger)
{
    private const int FirstServerError = 500;
    private const string NoOAuthError = "(none)";

    internal async Task<TokenExchange> ExchangeAsync(OidcOptions settings, string code, string codeVerifier, CancellationToken cancellationToken)
    {
        var response = await backChannel.RedeemCodeAsync(FormOf(settings, code, codeVerifier), cancellationToken);
        switch (response.Outcome)
        {
            case OidcBackChannelOutcome.Answered:
                return Interpret(response.StatusCode ?? 0, response.Body ?? string.Empty);
            case OidcBackChannelOutcome.Rejected:
                IdentityLog.OidcTokenEndpointRefused(logger, response.StatusCode ?? 0, response.FailureReason ?? NoOAuthError);
                return new TokenExchange(IdToken: null, Failure: OidcSignInResult.Rejected(OidcFailureReason.TokenRequestRejected));
            default:
                IdentityLog.OidcTokenEndpointUnreachable(logger, response.FailureReason ?? response.Outcome.ToString());
                return new TokenExchange(IdToken: null, Failure: OidcSignInResult.Unavailable());
        }
    }

    private TokenExchange Interpret(int status, string body)
    {
        if (status is < (int)System.Net.HttpStatusCode.OK or >= (int)System.Net.HttpStatusCode.MultipleChoices)
        {
            IdentityLog.OidcTokenEndpointRefused(logger, status, StringProperty(body, OidcProtocol.Error) ?? NoOAuthError);
            var failure = status >= FirstServerError
                ? OidcSignInResult.Unavailable()
                : OidcSignInResult.Rejected(OidcFailureReason.TokenRequestRejected);
            return new TokenExchange(IdToken: null, Failure: failure);
        }

        var idToken = StringProperty(body, OidcProtocol.IdToken);
        if (string.IsNullOrWhiteSpace(idToken))
        {
            IdentityLog.OidcTokenResponseInvalid(logger);
            return new TokenExchange(IdToken: null, Failure: OidcSignInResult.Rejected(OidcFailureReason.TokenResponseInvalid));
        }

        return new TokenExchange(idToken, Failure: null);
    }

    private static Dictionary<string, string> FormOf(OidcOptions settings, string code, string codeVerifier)
    {
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [OidcProtocol.GrantType] = OidcProtocol.AuthorizationCodeGrant,
            [OidcProtocol.Code] = code,
            [OidcProtocol.RedirectUri] = settings.RedirectUri,
            [OidcProtocol.ClientId] = settings.ClientId,
            [OidcProtocol.CodeVerifier] = codeVerifier,
        };
        if (!string.IsNullOrEmpty(settings.ClientSecret))
        {
            form[OidcProtocol.ClientSecret] = settings.ClientSecret;
        }

        return form;
    }

    /// <summary>A top-level JSON string property, or null for anything else (not JSON, not an object, another type).</summary>
    private static string? StringProperty(string json, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty(property, out var value)
                   && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

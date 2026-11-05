using System.Net.Http.Headers;
using Microsoft.Net.Http.Headers;
using Starter.DevIdp.Grants;
using Starter.DevIdp.Pages;
using Starter.DevIdp.Protocol;
using Starter.DevIdp.Token;

namespace Starter.DevIdp.UserInfo;

/// <summary>
/// The userinfo endpoint: a live access token from the token endpoint in an <c>Authorization: Bearer</c> header
/// returns the user's claims (sub, email, preferred_username, upn, name); anything else is 401 with
/// <c>WWW-Authenticate: Bearer error="invalid_token"</c>.
/// </summary>
internal sealed class UserInfoEndpoint
{
    private const string InvalidTokenChallenge = OidcValues.TokenTypeBearer + " error=\"" + OidcErrorCodes.InvalidToken + "\"";

    private readonly ExpiringStore<AccessGrant> _accessTokens;

    /// <summary>Creates the endpoint.</summary>
    public UserInfoEndpoint(ExpiringStore<AccessGrant> accessTokens) => _accessTokens = accessTokens;

    /// <summary>Handles one userinfo request.</summary>
    public IResult Handle(HttpContext context)
    {
        DevIdpResults.DisableCaching(context.Response);
        var grant = _accessTokens.Find(BearerToken(context.Request.Headers.Authorization.ToString()));
        if (grant is null)
        {
            context.Response.Headers[HeaderNames.WWWAuthenticate] = InvalidTokenChallenge;
            return DevIdpResults.ErrorJson(
                new OidcError(OidcErrorCodes.InvalidToken, "The access token is missing, unknown or expired."),
                StatusCodes.Status401Unauthorized);
        }

        return Results.Json(UserClaims.Of(grant.User));
    }

    private static string? BearerToken(string authorizationHeader) =>
        AuthenticationHeaderValue.TryParse(authorizationHeader, out var header)
        && string.Equals(header.Scheme, OidcValues.TokenTypeBearer, StringComparison.OrdinalIgnoreCase)
            ? header.Parameter
            : null;
}

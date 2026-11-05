using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Starter.DevIdp.Configuration;
using Starter.DevIdp.Grants;
using Starter.DevIdp.Keys;
using Starter.DevIdp.Protocol;

namespace Starter.DevIdp.Token;

/// <summary>
/// Issues the tokens for a redeemed code: an RS256 id_token (iss, aud = client id, azp, sub, iat, exp, nonce when the
/// request sent one, and the user's email, preferred_username, upn and name when set) and an opaque access token held
/// in memory for the userinfo endpoint. Times come from <see cref="TimeProvider"/>.
/// </summary>
internal sealed class TokenIssuer
{
    private readonly DevIdpSigningKey _signingKey;
    private readonly IOptions<DevIdpOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ExpiringStore<AccessGrant> _accessTokens;
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    /// <summary>Creates the issuer.</summary>
    public TokenIssuer(DevIdpSigningKey signingKey, IOptions<DevIdpOptions> options, TimeProvider timeProvider, ExpiringStore<AccessGrant> accessTokens)
    {
        _signingKey = signingKey;
        _options = options;
        _timeProvider = timeProvider;
        _accessTokens = accessTokens;
    }

    /// <summary>Issues the id_token and the access token for <paramref name="grant"/>.</summary>
    public TokenResponse Issue(AuthorizationGrant grant)
    {
        var lifetime = TimeSpan.FromSeconds(_options.Value.TokenLifetimeSeconds);
        var accessToken = _accessTokens.Add(new AccessGrant(grant.Request.Client.ClientId, grant.User), lifetime);
        return new TokenResponse
        {
            AccessToken = accessToken,
            TokenType = OidcValues.TokenTypeBearer,
            ExpiresIn = _options.Value.TokenLifetimeSeconds,
            IdToken = CreateIdToken(grant, lifetime),
            Scope = grant.Request.Scope,
        };
    }

    private string CreateIdToken(AuthorizationGrant grant, TimeSpan lifetime)
    {
        var now = _timeProvider.GetUtcNow();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Value.Issuer,
            Audience = grant.Request.Client.ClientId,
            IssuedAt = now.UtcDateTime,
            Expires = (now + lifetime).UtcDateTime,
            Claims = IdTokenClaims(grant),
            SigningCredentials = _signingKey.Credentials,
        };
        return _handler.CreateToken(descriptor);
    }

    private static Dictionary<string, object> IdTokenClaims(AuthorizationGrant grant)
    {
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [OidcClaimNames.AuthorizedParty] = grant.Request.Client.ClientId,
        };
        AddIfPresent(claims, OidcClaimNames.Nonce, grant.Request.Nonce);
        foreach (var claim in UserClaims.Of(grant.User))
        {
            claims[claim.Key] = claim.Value;
        }

        return claims;
    }

    private static void AddIfPresent(Dictionary<string, object> claims, string name, string? value)
    {
        if (value is not null)
        {
            claims[name] = value;
        }
    }
}

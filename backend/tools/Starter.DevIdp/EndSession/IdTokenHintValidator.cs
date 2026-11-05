using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Starter.DevIdp.Configuration;
using Starter.DevIdp.Keys;

namespace Starter.DevIdp.EndSession;

/// <summary>
/// Validates an <c>id_token_hint</c>: an id_token this IdP signed with its current key (RS256 only), issued by this
/// issuer to a registered client. Its lifetime is NOT checked, because OpenID Connect RP-Initiated Logout accepts an
/// expired id_token as a hint. A token signed by an earlier run (a new key every start) is refused.
/// </summary>
internal sealed class IdTokenHintValidator
{
    private readonly DevIdpSigningKey _signingKey;
    private readonly IOptions<DevIdpOptions> _options;
    private readonly JsonWebTokenHandler _handler = new();

    /// <summary>Creates the validator.</summary>
    public IdTokenHintValidator(DevIdpSigningKey signingKey, IOptions<DevIdpOptions> options)
    {
        _signingKey = signingKey;
        _options = options;
    }

    /// <summary>The client the hint was issued to, or <see langword="null"/> when the hint is not valid.</summary>
    public async Task<DevIdpClient?> ValidateAsync(string idTokenHint)
    {
        var options = _options.Value;
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudiences = options.Clients.Select(client => client.ClientId).ToArray(),
            IssuerSigningKey = _signingKey.PublicKey,
            ValidAlgorithms = [DevIdpSigningKey.Algorithm],
            ValidateLifetime = false,
            RequireExpirationTime = false,
        };
        var result = await _handler.ValidateTokenAsync(idTokenHint, parameters);
        if (!result.IsValid || result.SecurityToken is not JsonWebToken token)
        {
            return null;
        }

        return token.Audiences.Select(options.FindClient).FirstOrDefault(client => client is not null);
    }
}

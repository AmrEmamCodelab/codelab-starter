using System.Globalization;
using System.Security.Claims;
using Codelab.Security.Sessions;
using Codelab.Security.Signing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Infrastructure.Options;

namespace Starter.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Signs access tokens (JWT, HMAC-SHA256) carrying identity and session only: <c>sub</c>, <c>sid</c>, <c>sver</c>, a
/// unique <c>jti</c> and, while request signing is on, the session key id <c>skid</c>. Never a permission or a role:
/// permissions are resolved live per request.
/// </summary>
public sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> options, SessionKeyIdSource sessionKeys) : IAccessTokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();

    /// <inheritdoc />
    public string Issue(AccessTokenClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        var settings = options.Value;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = claims.IssuedAt.UtcDateTime,
            NotBefore = claims.IssuedAt.UtcDateTime,
            Expires = claims.ExpiresAt.UtcDateTime,
            Subject = new ClaimsIdentity(ClaimsOf(claims)),
            SigningCredentials = new SigningCredentials(SigningKey.Of(settings), SigningKey.Algorithm),
        };
        return _handler.CreateToken(descriptor);
    }

    private IEnumerable<Claim> ClaimsOf(AccessTokenClaims claims)
    {
        yield return new Claim(SecurityClaimTypes.Subject, claims.SubjectId.ToString());
        yield return new Claim(SecurityClaimTypes.SessionId, claims.SessionId);
        yield return new Claim(SecurityClaimTypes.SessionVersion, claims.SessionVersion.ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64);
        yield return new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString());
        if (sessionKeys.ActiveKeyId() is { } keyId)
        {
            yield return new Claim(SecurityClaimTypes.SessionKeyId, keyId);
        }
    }
}

/// <summary>
/// The session key id (<c>skid</c>) new sessions are bound to: the active key of the request-signing ring while signing
/// is on, otherwise none (the ring is not configured then, so it is resolved only when needed).
/// </summary>
public sealed class SessionKeyIdSource(IOptionsMonitor<RequestSigningOptions> signing, IServiceProvider services)
{
    /// <summary>The active signing key id, or null while request signing is off.</summary>
    public string? ActiveKeyId() =>
        signing.CurrentValue.Mode == SecurityEnforcementMode.Off
            ? null
            : services.GetRequiredService<ISigningKeyring>().ActiveKeyId;
}

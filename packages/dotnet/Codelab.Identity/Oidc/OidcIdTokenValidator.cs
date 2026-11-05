using Codelab.Identity.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Codelab.Identity.Oidc;

/// <summary>The outcome of an id_token validation. Stored and logged as an integer.</summary>
public enum IdTokenValidationStatus
{
    /// <summary>Every check passed.</summary>
    Valid = 1,

    /// <summary>A check failed: the token must not sign anyone in.</summary>
    Invalid = 2,

    /// <summary>No signing key set could be obtained; nothing about the token was decided.</summary>
    KeysUnavailable = 3,
}

/// <summary>A validation's status and, only when valid, the token.</summary>
public sealed record IdTokenValidationResult(IdTokenValidationStatus Status, JsonWebToken? Token)
{
    internal static IdTokenValidationResult Invalid { get; } = new(IdTokenValidationStatus.Invalid, Token: null);

    internal static IdTokenValidationResult KeysUnavailable { get; } = new(IdTokenValidationStatus.KeysUnavailable, Token: null);
}

/// <summary>
/// Validates an id_token for one sign-in: the header algorithm is on the asymmetric allowlist BEFORE any key is
/// looked at (so <c>none</c> and every HMAC algorithm are refused outright, whatever keys are configured), the signature
/// verifies against the key source, then issuer, audience, <c>azp</c>, lifetime (on the injected clock, with
/// <see cref="OidcOptions.ClockSkewSeconds"/>), subject and nonce (constant time). Never throws for a bad token.
/// </summary>
public sealed class OidcIdTokenValidator
{
    private readonly IOptions<OidcOptions> options;
    private readonly IIdTokenSigningKeySource signingKeys;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<OidcIdTokenValidator> logger;

    /// <summary>A validator over the configured options and key source.</summary>
    public OidcIdTokenValidator(IOptions<OidcOptions> options, IIdTokenSigningKeySource signingKeys, TimeProvider timeProvider, ILogger<OidcIdTokenValidator> logger)
    {
        this.options = options;
        this.signingKeys = signingKeys;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <summary>Validates <paramref name="idToken"/> for the sign-in that sent <paramref name="expectedNonce"/>.</summary>
    public async Task<IdTokenValidationResult> ValidateAsync(string idToken, string expectedNonce, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(expectedNonce);
        var settings = options.Value;
        var handler = new JsonWebTokenHandler { MapInboundClaims = false };
        if (Parse(handler, idToken) is not { } parsed)
        {
            return Refuse("not a well-formed JWT");
        }

        var allowed = settings.EffectiveAllowedAlgorithms.Where(OidcAlgorithms.IsAsymmetric).ToArray();
        if (!allowed.Contains(parsed.Alg, StringComparer.Ordinal))
        {
            return Refuse("the algorithm is not on the allowlist");
        }

        var keys = await signingKeys.GetSigningKeysAsync(string.IsNullOrEmpty(parsed.Kid) ? null : parsed.Kid, cancellationToken);
        if (keys is null)
        {
            IdentityLog.OidcSigningKeysUnavailable(logger, settings.SigningKeyMode);
            return IdTokenValidationResult.KeysUnavailable;
        }

        var result = await handler.ValidateTokenAsync(idToken, ParametersFor(settings, keys, allowed));
        if (!result.IsValid)
        {
            return Refuse(result.Exception?.GetType().Name ?? "signature, issuer or audience");
        }

        var token = (JsonWebToken)result.SecurityToken;
        var problem = IdTokenClaimRules.Problem(token, settings, expectedNonce, timeProvider.GetUtcNow());
        return problem is null ? new IdTokenValidationResult(IdTokenValidationStatus.Valid, token) : Refuse(problem);
    }

    private static JsonWebToken? Parse(JsonWebTokenHandler handler, string idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken) || !handler.CanReadToken(idToken))
        {
            return null;
        }

        try
        {
            return handler.ReadJsonWebToken(idToken);
        }
        catch (Exception failure) when (failure is ArgumentException or SecurityTokenMalformedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Signature, issuer and audience are checked by IdentityModel; lifetime is not (it reads the system clock), the
    /// claim rules check it on the injected clock instead.
    /// </summary>
    private static TokenValidationParameters ParametersFor(OidcOptions settings, IReadOnlyList<SecurityKey> keys, string[] allowed) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = settings.Authority,
        ValidateAudience = true,
        ValidAudience = settings.ClientId,
        ValidateLifetime = false,
        RequireExpirationTime = false,
        RequireSignedTokens = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKeys = keys,
        TryAllIssuerSigningKeys = true,
        ValidAlgorithms = allowed,
    };

    private IdTokenValidationResult Refuse(string reason)
    {
        IdentityLog.OidcIdTokenRefused(logger, reason);
        return IdTokenValidationResult.Invalid;
    }
}

/// <summary>The claim checks after the signature: lifetime on the injected clock, subject, nonce and authorized party.</summary>
internal static class IdTokenClaimRules
{
    /// <summary>Why the token is refused, or null when every rule holds.</summary>
    internal static string? Problem(JsonWebToken token, OidcOptions settings, string expectedNonce, DateTimeOffset now) =>
        LifetimeProblem(token, now, settings.ClockSkew)
        ?? (string.IsNullOrWhiteSpace(token.Subject) ? "no subject" : null)
        ?? (Pkce.FixedTimeEquals(StringClaim(token, OidcClaimNames.Nonce), expectedNonce) ? null : "the nonce does not match this sign-in")
        ?? AuthorizedPartyProblem(token, settings.ClientId);

    /// <summary>A JSON string claim, trimmed; null when absent, blank or of another JSON type.</summary>
    internal static string? StringClaim(JsonWebToken token, string name) =>
        token.TryGetPayloadValue<object>(name, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    /// <summary>A JSON boolean claim, or the string <c>true</c>/<c>false</c>; false otherwise.</summary>
    internal static bool BooleanClaim(JsonWebToken token, string name) =>
        token.TryGetPayloadValue<object>(name, out var value)
        && (value is true || (value is string text && bool.TryParse(text, out var parsed) && parsed));

    private static string? LifetimeProblem(JsonWebToken token, DateTimeOffset now, TimeSpan skew)
    {
        if (!TryEpoch(token, OidcClaimNames.Expires, out var expires))
        {
            return "no exp";
        }

        if (now - skew >= expires)
        {
            return "expired";
        }

        if (TryEpoch(token, OidcClaimNames.NotBefore, out var notBefore) && now + skew < notBefore)
        {
            return "not yet valid";
        }

        if (TryEpoch(token, OidcClaimNames.IssuedAt, out var issuedAt) && now + skew < issuedAt)
        {
            return "issued in the future";
        }

        return null;
    }

    /// <summary>OIDC Core id_token validation, rules 4 and 5: several audiences need <c>azp</c>; an <c>azp</c> present must be this client.</summary>
    private static string? AuthorizedPartyProblem(JsonWebToken token, string clientId)
    {
        var authorizedParty = StringClaim(token, OidcClaimNames.AuthorizedParty);
        var multiAudience = token.Audiences.Skip(1).Any();
        if (multiAudience && authorizedParty is null)
        {
            return "several audiences and no azp";
        }

        return authorizedParty is not null && !string.Equals(authorizedParty, clientId, StringComparison.Ordinal) ? "azp is not this client" : null;
    }

    private static bool TryEpoch(JsonWebToken token, string name, out DateTimeOffset instant)
    {
        instant = default;
        if (!token.TryGetPayloadValue<long>(name, out var seconds))
        {
            return false;
        }

        instant = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return true;
    }
}

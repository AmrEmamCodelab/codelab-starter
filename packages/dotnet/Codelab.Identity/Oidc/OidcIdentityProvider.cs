using Codelab.Identity.Diagnostics;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Codelab.Identity.Oidc;

/// <summary>
/// <see cref="IOidcIdentityProvider"/>: authorization URL, back-channel code redemption and end_session URL. The
/// browser-facing state (state, nonce, verifier, return path) is the host's to seal; this class keeps nothing between
/// calls, so a callback may land on any node.
/// </summary>
internal sealed class OidcIdentityProvider : IOidcIdentityProvider
{
    private readonly IOptions<OidcOptions> options;
    private readonly OidcIdTokenValidator validator;
    private readonly OidcTokenClient tokens;
    private readonly ILogger<OidcIdentityProvider> logger;

    public OidcIdentityProvider(
        IOptions<OidcOptions> options,
        OidcIdTokenValidator validator,
        IHttpClientFactory clients,
        ILogger<OidcIdentityProvider> logger)
    {
        this.options = options;
        this.validator = validator;
        this.logger = logger;
        tokens = new OidcTokenClient(clients, logger);
    }

    public OidcAuthorizationRequest BeginAuthorization(string returnUrl)
    {
        var settings = EnabledSettings();
        var (state, nonce, verifier) = (Pkce.NewOpaqueValue(), Pkce.NewOpaqueValue(), Pkce.NewVerifier());
        var query = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [OidcProtocol.ResponseType] = OidcProtocol.CodeResponseType,
            [OidcProtocol.ClientId] = settings.ClientId,
            [OidcProtocol.RedirectUri] = settings.RedirectUri,
            [OidcProtocol.Scope] = string.Join(OidcProtocol.ScopeSeparator, settings.EffectiveScopes),
            [OidcProtocol.State] = state,
            [OidcProtocol.Nonce] = nonce,
            [OidcProtocol.CodeChallenge] = Pkce.ChallengeOf(verifier),
            [OidcProtocol.CodeChallengeMethod] = OidcProtocol.S256,
        };
        var url = new Uri(QueryHelpers.AddQueryString(settings.AuthorizationEndpoint, query));
        return new OidcAuthorizationRequest(url, state, verifier, nonce, ReturnUrls.Sanitize(returnUrl));
    }

    public async Task<OidcSignInResult> CompleteAsync(OidcCallback callback, OidcPendingAuthorization pending, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(pending);
        var settings = EnabledSettings();
        if (!Pkce.FixedTimeEquals(callback.State, pending.State) || string.IsNullOrEmpty(pending.State))
        {
            IdentityLog.OidcStateMismatch(logger);
            return OidcSignInResult.Rejected(OidcFailureReason.StateMismatch);
        }

        if (string.IsNullOrWhiteSpace(callback.Code))
        {
            return OidcSignInResult.Rejected(OidcFailureReason.MissingCode);
        }

        var exchange = await tokens.ExchangeAsync(settings, callback.Code, pending.CodeVerifier, cancellationToken);
        if (exchange.Failure is { } failure)
        {
            return failure;
        }

        var validation = await validator.ValidateAsync(exchange.IdToken!, pending.Nonce, cancellationToken);
        return validation.Status switch
        {
            IdTokenValidationStatus.Valid => OidcSignInResult.Succeeded(IdentityOf(validation.Token!, settings, exchange.IdToken!)),
            IdTokenValidationStatus.KeysUnavailable => OidcSignInResult.Unavailable(),
            _ => OidcSignInResult.Rejected(OidcFailureReason.IdTokenInvalid),
        };
    }

    public Uri BuildEndSessionUrl(string idTokenHint, Uri? postLogoutRedirectUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idTokenHint);
        var settings = EnabledSettings();
        var query = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [OidcProtocol.IdTokenHint] = idTokenHint,
            [OidcProtocol.ClientId] = settings.ClientId,
        };
        var redirect = postLogoutRedirectUri?.AbsoluteUri ?? NonBlank(settings.PostLogoutRedirectUri);
        if (redirect is not null)
        {
            query[OidcProtocol.PostLogoutRedirectUri] = redirect;
        }

        return new Uri(QueryHelpers.AddQueryString(settings.EndSessionEndpoint, query));
    }

    /// <summary>The identity values, read from the claims the configuration names.</summary>
    internal static OidcIdentity IdentityOf(JsonWebToken token, OidcOptions settings, string rawIdToken)
    {
        var claims = settings.Claims;
        var email = IdTokenClaimRules.StringClaim(token, claims.Email);
        var emailUsable = !settings.RequireVerifiedEmail || IdTokenClaimRules.BooleanClaim(token, claims.EmailVerified);
        return new OidcIdentity(
            Subject: token.Subject,
            Email: emailUsable ? email : null,
            LoginName: IdTokenClaimRules.StringClaim(token, claims.LoginName),
            Upn: IdTokenClaimRules.StringClaim(token, claims.Upn),
            DisplayName: IdTokenClaimRules.StringClaim(token, claims.DisplayName),
            IdToken: rawIdToken);
    }

    private OidcOptions EnabledSettings()
    {
        var settings = options.Value;
        return settings.Enabled
            ? settings
            : throw new InvalidOperationException($"OIDC sign-in is not enabled ({OidcOptions.SectionName}:Enabled is false).");
    }

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

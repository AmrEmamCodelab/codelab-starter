using System.Text.Json.Serialization;
using Starter.DevIdp.Configuration;
using Starter.DevIdp.Keys;
using Starter.DevIdp.Protocol;

namespace Starter.DevIdp.Discovery;

/// <summary>The OpenID Provider metadata served at <c>/.well-known/openid-configuration</c>.</summary>
internal sealed class DiscoveryDocument
{
    /// <summary><c>issuer</c>.</summary>
    [JsonPropertyName("issuer")]
    public required string Issuer { get; init; }

    /// <summary><c>authorization_endpoint</c>.</summary>
    [JsonPropertyName("authorization_endpoint")]
    public required string AuthorizationEndpoint { get; init; }

    /// <summary><c>token_endpoint</c>.</summary>
    [JsonPropertyName("token_endpoint")]
    public required string TokenEndpoint { get; init; }

    /// <summary><c>jwks_uri</c>.</summary>
    [JsonPropertyName("jwks_uri")]
    public required string JwksUri { get; init; }

    /// <summary><c>userinfo_endpoint</c>.</summary>
    [JsonPropertyName("userinfo_endpoint")]
    public required string UserInfoEndpoint { get; init; }

    /// <summary><c>end_session_endpoint</c>.</summary>
    [JsonPropertyName("end_session_endpoint")]
    public required string EndSessionEndpoint { get; init; }

    /// <summary><c>response_types_supported</c>.</summary>
    [JsonPropertyName("response_types_supported")]
    public IReadOnlyList<string> ResponseTypesSupported { get; } = [OidcValues.ResponseTypeCode];

    /// <summary><c>grant_types_supported</c>.</summary>
    [JsonPropertyName("grant_types_supported")]
    public IReadOnlyList<string> GrantTypesSupported { get; } = [OidcValues.GrantTypeAuthorizationCode];

    /// <summary><c>subject_types_supported</c>.</summary>
    [JsonPropertyName("subject_types_supported")]
    public IReadOnlyList<string> SubjectTypesSupported { get; } = [OidcValues.SubjectTypePublic];

    /// <summary><c>id_token_signing_alg_values_supported</c>.</summary>
    [JsonPropertyName("id_token_signing_alg_values_supported")]
    public IReadOnlyList<string> SigningAlgorithmsSupported { get; } = [DevIdpSigningKey.Algorithm];

    /// <summary><c>code_challenge_methods_supported</c>.</summary>
    [JsonPropertyName("code_challenge_methods_supported")]
    public IReadOnlyList<string> CodeChallengeMethodsSupported { get; } = [OidcValues.CodeChallengeMethodS256];

    /// <summary><c>scopes_supported</c>.</summary>
    [JsonPropertyName("scopes_supported")]
    public IReadOnlyList<string> ScopesSupported { get; } = [OidcValues.ScopeOpenId, OidcValues.ScopeProfile, OidcValues.ScopeEmail];

    /// <summary><c>token_endpoint_auth_methods_supported</c>.</summary>
    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public IReadOnlyList<string> TokenEndpointAuthMethodsSupported { get; } =
        [OidcValues.ClientAuthSecretBasic, OidcValues.ClientAuthSecretPost, OidcValues.ClientAuthNone];

    /// <summary><c>claims_supported</c>.</summary>
    [JsonPropertyName("claims_supported")]
    public IReadOnlyList<string> ClaimsSupported { get; } =
    [
        OidcClaimNames.Subject, OidcClaimNames.Email, OidcClaimNames.PreferredUserName, OidcClaimNames.Upn,
        OidcClaimNames.Name, OidcClaimNames.Nonce, OidcClaimNames.AuthorizedParty,
    ];

    /// <summary>The metadata for the issuer in <paramref name="options"/>.</summary>
    public static DiscoveryDocument For(DevIdpOptions options)
    {
        var issuerBase = options.IssuerBase;
        return new DiscoveryDocument
        {
            Issuer = options.Issuer,
            AuthorizationEndpoint = issuerBase + OidcRoutes.Authorize,
            TokenEndpoint = issuerBase + OidcRoutes.Token,
            JwksUri = issuerBase + OidcRoutes.Jwks,
            UserInfoEndpoint = issuerBase + OidcRoutes.UserInfo,
            EndSessionEndpoint = issuerBase + OidcRoutes.EndSession,
        };
    }
}

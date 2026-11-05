using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;

namespace Starter.DevIdp.Keys;

/// <summary>The JSON Web Key Set served at <c>/jwks</c>.</summary>
/// <param name="Keys">The published keys (one: the current signing key).</param>
internal sealed record JwksDocument([property: JsonPropertyName(JsonWebKeySetParameterNames.Keys)] IReadOnlyList<JwksKey> Keys);

/// <summary>One public RSA signing key as a JWK.</summary>
internal sealed class JwksKey
{
    /// <summary><c>kty</c>.</summary>
    [JsonPropertyName(JsonWebKeyParameterNames.Kty)]
    public required string KeyType { get; init; }

    /// <summary><c>use</c>.</summary>
    [JsonPropertyName(JsonWebKeyParameterNames.Use)]
    public required string Use { get; init; }

    /// <summary><c>alg</c>.</summary>
    [JsonPropertyName(JsonWebKeyParameterNames.Alg)]
    public required string Algorithm { get; init; }

    /// <summary><c>kid</c>.</summary>
    [JsonPropertyName(JsonWebKeyParameterNames.Kid)]
    public required string KeyId { get; init; }

    /// <summary><c>n</c> (base64url).</summary>
    [JsonPropertyName(JsonWebKeyParameterNames.N)]
    public required string Modulus { get; init; }

    /// <summary><c>e</c> (base64url).</summary>
    [JsonPropertyName(JsonWebKeyParameterNames.E)]
    public required string Exponent { get; init; }
}

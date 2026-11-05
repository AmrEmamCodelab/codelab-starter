using System.Text.Json.Serialization;

namespace Starter.DevIdp.Token;

/// <summary>The token endpoint's success body.</summary>
internal sealed class TokenResponse
{
    /// <summary>The opaque access token for the userinfo endpoint.</summary>
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    /// <summary>Always <c>Bearer</c>.</summary>
    [JsonPropertyName("token_type")]
    public required string TokenType { get; init; }

    /// <summary>The access token lifetime in seconds.</summary>
    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; init; }

    /// <summary>The RS256 id_token.</summary>
    [JsonPropertyName("id_token")]
    public required string IdToken { get; init; }

    /// <summary>The granted scope.</summary>
    [JsonPropertyName("scope")]
    public required string Scope { get; init; }
}

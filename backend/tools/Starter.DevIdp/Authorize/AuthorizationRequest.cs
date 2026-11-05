using Starter.DevIdp.Configuration;
using Starter.DevIdp.Protocol;

namespace Starter.DevIdp.Authorize;

/// <summary>A validated authorization request (response_type=code, S256 PKCE, a registered client and redirect URI).</summary>
internal sealed class AuthorizationRequest
{
    /// <summary>The registered client.</summary>
    public required DevIdpClient Client { get; init; }

    /// <summary>The registered redirect URI the request named.</summary>
    public required string RedirectUri { get; init; }

    /// <summary>The requested scope (contains <c>openid</c>).</summary>
    public required string Scope { get; init; }

    /// <summary>The S256 code challenge.</summary>
    public required string CodeChallenge { get; init; }

    /// <summary>The state to echo on the redirect, if sent.</summary>
    public string? State { get; init; }

    /// <summary>The nonce to echo in the id_token, if sent.</summary>
    public string? Nonce { get; init; }

    /// <summary>The login hint, if sent: signs in without the picker.</summary>
    public string? LoginHint { get; init; }

    /// <summary>
    /// The protocol parameters of this request, as the picker form carries them back to POST /authorize (without the
    /// login hint: the chosen user's button supplies it).
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> ToFormFields()
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new(OidcParameterNames.ClientId, Client.ClientId),
            new(OidcParameterNames.RedirectUri, RedirectUri),
            new(OidcParameterNames.ResponseType, OidcValues.ResponseTypeCode),
            new(OidcParameterNames.Scope, Scope),
            new(OidcParameterNames.CodeChallenge, CodeChallenge),
            new(OidcParameterNames.CodeChallengeMethod, OidcValues.CodeChallengeMethodS256),
        };
        AddIfPresent(fields, OidcParameterNames.State, State);
        AddIfPresent(fields, OidcParameterNames.Nonce, Nonce);
        return fields;
    }

    private static void AddIfPresent(List<KeyValuePair<string, string>> fields, string name, string? value)
    {
        if (value is not null)
        {
            fields.Add(new(name, value));
        }
    }
}

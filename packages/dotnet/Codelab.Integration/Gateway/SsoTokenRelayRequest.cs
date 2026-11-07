namespace Codelab.Integration.Gateway;

/// <summary>
/// The OIDC token request relayed through POST <see cref="GatewayRoutes.SsoToken"/>: the form fields of the token
/// request (grant_type, code, redirect_uri, code_verifier, client_id, client_secret, ...) passed through unchanged, in
/// order. The gateway forwards them to the provider's token endpoint and returns the provider's JSON verbatim.
/// </summary>
/// <remarks>The fields carry secrets (the code, the verifier, the client secret): never log them.</remarks>
public sealed class SsoTokenRelayRequest
{
    /// <summary>Creates the request from the token request form fields.</summary>
    /// <exception cref="ArgumentException">No field, a blank field name, or more than <see cref="GatewayRelayLimits.MaxSsoTokenFormFields"/> fields.</exception>
    public SsoTokenRelayRequest(IEnumerable<KeyValuePair<string, string>> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        Fields = [.. fields];
        if (Fields.Count == 0 || Fields.Count > GatewayRelayLimits.MaxSsoTokenFormFields)
        {
            throw new ArgumentException($"A token request has 1 to {GatewayRelayLimits.MaxSsoTokenFormFields} fields.", nameof(fields));
        }

        if (Fields.Any(field => string.IsNullOrWhiteSpace(field.Key)))
        {
            throw new ArgumentException("A token request field has a blank name.", nameof(fields));
        }
    }

    /// <summary>The form fields, in order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Fields { get; }

    /// <summary>The form body (<see cref="GatewayMediaTypes.FormUrlEncoded"/>).</summary>
    public FormUrlEncodedContent ToContent() => new(Fields);
}

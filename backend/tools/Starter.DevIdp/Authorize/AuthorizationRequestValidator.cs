using Starter.DevIdp.Configuration;
using Starter.DevIdp.Protocol;

namespace Starter.DevIdp.Authorize;

/// <summary>The outcome of validating an authorization request.</summary>
/// <param name="Request">The validated request, when valid.</param>
/// <param name="Error">The error, when invalid.</param>
/// <param name="RedirectUri">
/// Where the error may be sent (the client's registered redirect URI), or <see langword="null"/> when the client or
/// redirect URI could not be trusted: such an error is shown on an IdP page and never redirected (RFC 6749, authorization error response).
/// </param>
/// <param name="State">The state to echo with a redirected error.</param>
internal sealed record AuthorizationValidation(AuthorizationRequest? Request, OidcError? Error, string? RedirectUri, string? State)
{
    /// <summary>A valid request.</summary>
    public static AuthorizationValidation Valid(AuthorizationRequest request) => new(request, Error: null, RedirectUri: null, State: null);

    /// <summary>An error shown on an IdP page.</summary>
    public static AuthorizationValidation Untrusted(OidcError error) => new(Request: null, error, RedirectUri: null, State: null);
}

/// <summary>
/// Validates an authorization request in the order RFC 6749 requires: first the client and the redirect URI (an
/// error there is never redirected), then response_type=code, the openid scope and an S256 PKCE challenge (an error
/// there is redirected to the client with the state).
/// </summary>
internal static class AuthorizationRequestValidator
{
    /// <summary>Validates <paramref name="parameters"/> against the registered clients in <paramref name="options"/>.</summary>
    public static AuthorizationValidation Validate(OidcParameters parameters, DevIdpOptions options)
    {
        var client = options.FindClient(parameters.Get(OidcParameterNames.ClientId));
        if (client is null)
        {
            return AuthorizationValidation.Untrusted(new OidcError(OidcErrorCodes.InvalidClient, "The client_id is missing or not registered."));
        }

        var redirectUri = parameters.Get(OidcParameterNames.RedirectUri);
        if (!client.AllowsRedirect(redirectUri))
        {
            return AuthorizationValidation.Untrusted(new OidcError(OidcErrorCodes.InvalidRequest, "The redirect_uri is missing or not registered for this client."));
        }

        var state = parameters.Get(OidcParameterNames.State);
        var protocolError = ValidateProtocol(parameters);
        if (protocolError is not null)
        {
            return new AuthorizationValidation(Request: null, protocolError, redirectUri, state);
        }

        return AuthorizationValidation.Valid(new AuthorizationRequest
        {
            Client = client,
            RedirectUri = redirectUri!,
            Scope = parameters.Get(OidcParameterNames.Scope)!,
            CodeChallenge = parameters.Get(OidcParameterNames.CodeChallenge)!,
            State = state,
            Nonce = parameters.Get(OidcParameterNames.Nonce),
            LoginHint = parameters.Get(OidcParameterNames.LoginHint),
        });
    }

    private static OidcError? ValidateProtocol(OidcParameters parameters)
    {
        if (!string.Equals(parameters.Get(OidcParameterNames.ResponseType), OidcValues.ResponseTypeCode, StringComparison.Ordinal))
        {
            return new OidcError(OidcErrorCodes.UnsupportedResponseType, "Only response_type=code is supported.");
        }

        if (!HasOpenIdScope(parameters.Get(OidcParameterNames.Scope)))
        {
            return new OidcError(OidcErrorCodes.InvalidScope, "The scope must include openid.");
        }

        if (!Pkce.IsWellFormed(parameters.Get(OidcParameterNames.CodeChallenge)))
        {
            return new OidcError(OidcErrorCodes.InvalidRequest, "A code_challenge of 43 to 128 unreserved characters is required.");
        }

        if (!string.Equals(parameters.Get(OidcParameterNames.CodeChallengeMethod), OidcValues.CodeChallengeMethodS256, StringComparison.Ordinal))
        {
            return new OidcError(OidcErrorCodes.InvalidRequest, "Only code_challenge_method=S256 is supported.");
        }

        return null;
    }

    private static bool HasOpenIdScope(string? scope) =>
        scope is not null
        && scope.Split(OidcValues.ScopeSeparator, StringSplitOptions.RemoveEmptyEntries).Contains(OidcValues.ScopeOpenId, StringComparer.Ordinal);
}

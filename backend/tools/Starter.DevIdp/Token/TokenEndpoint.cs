using Microsoft.Extensions.Options;
using Starter.DevIdp.Configuration;
using Starter.DevIdp.Grants;
using Starter.DevIdp.Pages;
using Starter.DevIdp.Protocol;

namespace Starter.DevIdp.Token;

/// <summary>
/// The token endpoint (authorization_code grant only). It authenticates the client, redeems the code once (a code is
/// gone after the first attempt, even a failed one), checks that the code was issued to this client and redirect URI,
/// verifies the PKCE verifier against the S256 challenge, then issues the tokens. Errors are OAuth JSON:
/// invalid_request, unsupported_grant_type, invalid_client (401) or invalid_grant.
/// </summary>
internal sealed class TokenEndpoint
{
    private readonly IOptions<DevIdpOptions> _options;
    private readonly ExpiringStore<AuthorizationGrant> _codes;
    private readonly TokenIssuer _issuer;

    /// <summary>Creates the endpoint.</summary>
    public TokenEndpoint(IOptions<DevIdpOptions> options, ExpiringStore<AuthorizationGrant> codes, TokenIssuer issuer)
    {
        _options = options;
        _codes = codes;
        _issuer = issuer;
    }

    /// <summary>Handles one token request.</summary>
    public async Task<IResult> HandleAsync(HttpContext context)
    {
        DevIdpResults.DisableCaching(context.Response);
        if (!context.Request.HasFormContentType)
        {
            return DevIdpResults.ErrorJson(new OidcError(OidcErrorCodes.InvalidRequest, "The token request must be form-encoded."));
        }

        var form = OidcParameters.From(await context.Request.ReadFormAsync(context.RequestAborted));
        if (!string.Equals(form.Get(OidcParameterNames.GrantType), OidcValues.GrantTypeAuthorizationCode, StringComparison.Ordinal))
        {
            return DevIdpResults.ErrorJson(new OidcError(OidcErrorCodes.UnsupportedGrantType, "Only grant_type=authorization_code is supported."));
        }

        var authorization = context.Request.Headers.Authorization;
        var client = ClientAuthenticator.Authenticate(form, authorization.Count == 0 ? null : authorization.ToString(), _options.Value);
        if (client is null)
        {
            return DevIdpResults.ErrorJson(
                new OidcError(OidcErrorCodes.InvalidClient, "The client is unknown or its credentials are wrong."),
                StatusCodes.Status401Unauthorized);
        }

        var grant = Redeem(form, client);
        return grant is null
            ? DevIdpResults.ErrorJson(new OidcError(OidcErrorCodes.InvalidGrant, "The code is invalid, expired, already used, or does not match this request."))
            : Results.Json(_issuer.Issue(grant));
    }

    private AuthorizationGrant? Redeem(OidcParameters form, DevIdpClient client)
    {
        var grant = _codes.Take(form.Get(OidcParameterNames.Code));
        if (grant is null)
        {
            return null;
        }

        var request = grant.Request;
        var matches = string.Equals(request.Client.ClientId, client.ClientId, StringComparison.Ordinal)
            && string.Equals(request.RedirectUri, form.Get(OidcParameterNames.RedirectUri), StringComparison.Ordinal)
            && Pkce.Verify(form.Get(OidcParameterNames.CodeVerifier), request.CodeChallenge);
        return matches ? grant : null;
    }
}

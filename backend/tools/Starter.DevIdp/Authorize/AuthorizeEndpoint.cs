using Microsoft.Extensions.Options;
using Starter.DevIdp.Configuration;
using Starter.DevIdp.Grants;
using Starter.DevIdp.Pages;
using Starter.DevIdp.Protocol;

namespace Starter.DevIdp.Authorize;

/// <summary>
/// The authorization endpoint. GET carries the request in the query, POST carries it back from the picker form.
/// A valid request with a <c>login_hint</c> that names a fixture user signs in at once (the non-interactive path tests
/// use); without a hint it renders the picker; an unknown hint redirects <c>access_denied</c>. Signing in issues a
/// one-time code and redirects to the client with the code and the state.
/// </summary>
internal sealed class AuthorizeEndpoint
{
    private readonly IOptions<DevIdpOptions> _options;
    private readonly ExpiringStore<AuthorizationGrant> _codes;

    /// <summary>Creates the endpoint.</summary>
    public AuthorizeEndpoint(IOptions<DevIdpOptions> options, ExpiringStore<AuthorizationGrant> codes)
    {
        _options = options;
        _codes = codes;
    }

    /// <summary>Handles the picker form posted back to the endpoint.</summary>
    public async Task<IResult> HandleFormAsync(HttpRequest request)
    {
        if (!request.HasFormContentType)
        {
            return DevIdpResults.ErrorPage(new OidcError(OidcErrorCodes.InvalidRequest, "A POST to the authorization endpoint must be form-encoded."));
        }

        return Handle(OidcParameters.From(await request.ReadFormAsync(request.HttpContext.RequestAborted)));
    }

    /// <summary>Handles one authorization request.</summary>
    public IResult Handle(OidcParameters parameters)
    {
        var options = _options.Value;
        var validation = AuthorizationRequestValidator.Validate(parameters, options);
        if (validation.Request is null)
        {
            return validation.RedirectUri is null
                ? DevIdpResults.ErrorPage(validation.Error!)
                : DevIdpResults.RedirectError(validation.RedirectUri, validation.Error!, validation.State);
        }

        var request = validation.Request;
        if (request.LoginHint is null)
        {
            return DevIdpResults.Html(DevIdpPages.Picker(request, options.Users));
        }

        var user = options.FindUser(request.LoginHint);
        if (user is null)
        {
            var error = new OidcError(OidcErrorCodes.AccessDenied, "The login_hint names no fixture user.");
            return DevIdpResults.RedirectError(request.RedirectUri, error, request.State);
        }

        var code = _codes.Add(new AuthorizationGrant(request, user), TimeSpan.FromSeconds(options.CodeLifetimeSeconds));
        return DevIdpResults.RedirectWith(
            request.RedirectUri,
            new(OidcParameterNames.Code, code),
            new(OidcParameterNames.State, request.State));
    }
}

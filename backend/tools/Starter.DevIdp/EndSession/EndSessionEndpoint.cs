using Starter.DevIdp.Pages;
using Starter.DevIdp.Protocol;

namespace Starter.DevIdp.EndSession;

/// <summary>
/// RP-initiated logout. With no <c>id_token_hint</c> it shows the signed-out page. A hint that this IdP did not sign
/// for a registered client (or that names another client than <c>client_id</c>) is refused with an error page (400,
/// no redirect). With a valid hint it redirects to <c>post_logout_redirect_uri</c>, with the state, only when that URI
/// is registered for the hint's client; otherwise it shows the signed-out page. The IdP keeps no session, so there is
/// nothing else to end.
/// </summary>
internal sealed class EndSessionEndpoint
{
    private readonly IdTokenHintValidator _hintValidator;

    /// <summary>Creates the endpoint.</summary>
    public EndSessionEndpoint(IdTokenHintValidator hintValidator) => _hintValidator = hintValidator;

    /// <summary>Handles one end-session request.</summary>
    public async Task<IResult> HandleAsync(OidcParameters parameters)
    {
        var hint = parameters.Get(OidcParameterNames.IdTokenHint);
        if (hint is null)
        {
            return DevIdpResults.Html(DevIdpPages.SignedOut("No id_token_hint was sent, so there is no client to return to."));
        }

        var client = await _hintValidator.ValidateAsync(hint);
        var clientId = parameters.Get(OidcParameterNames.ClientId);
        if (client is null || (clientId is not null && !string.Equals(clientId, client.ClientId, StringComparison.Ordinal)))
        {
            return DevIdpResults.ErrorPage(new OidcError(OidcErrorCodes.InvalidRequest, "The id_token_hint is not valid for a registered client."));
        }

        var postLogoutRedirectUri = parameters.Get(OidcParameterNames.PostLogoutRedirectUri);
        if (postLogoutRedirectUri is null)
        {
            return DevIdpResults.Html(DevIdpPages.SignedOut(note: null));
        }

        return client.AllowsPostLogoutRedirect(postLogoutRedirectUri)
            ? DevIdpResults.RedirectWith(postLogoutRedirectUri, new KeyValuePair<string, string?>(OidcParameterNames.State, parameters.Get(OidcParameterNames.State)))
            : DevIdpResults.Html(DevIdpPages.SignedOut("The post_logout_redirect_uri is not registered for this client, so no redirect was made."));
    }
}

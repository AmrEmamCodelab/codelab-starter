using Codelab.Security.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Results;
using Starter.Modules.Identity.Application.LoginModes;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Api.LoginModes;

/// <summary>Which login methods the login page offers. Anonymous (read before sign-in; modes only, never an identity).</summary>
[ApiController]
[EnableRateLimiting(ClientRateLimitOptions.PolicyName)]
[Route(AuthRoutes.Base)]
[Produces(AuthRoutes.JsonContentType)]
public sealed class LoginModesController(LoginModesQuery query) : ControllerBase
{
    /// <summary>The switched-on login modes (<c>Auth:Modes</c>).</summary>
    [AllowAnonymous]
    [HttpGet(LoginModeRoutes.Modes)]
    [ProducesResponseType<LoginModesResponse>(StatusCodes.Status200OK)]
    public ActionResult<LoginModesResponse> GetModes() => LoginModesResponse.From(query.Execute());
}

/// <summary>
/// Active Directory login: the directory checks the password; the answer is the same session body as basic login.
/// Anonymous and under the per-client rate limit; 404 while <c>ad</c> is not in <c>Auth:Modes</c>.
/// </summary>
[ApiController]
[EnableRateLimiting(ClientRateLimitOptions.PolicyName)]
[RequiresLoginMode(SignInMode.ActiveDirectory)]
[Route(AuthRoutes.Base)]
[Produces(AuthRoutes.JsonContentType)]
public sealed class DirectoryLoginController(DirectoryLoginUseCase login) : ControllerBase
{
    /// <summary>Signs in with a directory user name and password.</summary>
    [AllowAnonymous]
    [HttpPost(LoginModeRoutes.DirectoryLogin)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SessionResponse>> LoginAsync([FromBody] DirectoryLoginCommand command, CancellationToken cancellationToken) =>
        LoginModeResults.ToActionResult(await login.ExecuteAsync(command, cancellationToken), LoginModeResults.Session);
}

/// <summary>
/// OpenID Connect single sign-on (code flow + PKCE S256). <c>sso/login</c> seals the sign-in into an HttpOnly cookie
/// and redirects to the provider; <c>sso/signin-oidc</c> (the registered redirect URI) completes it server-side and
/// redirects to the SPA with a one-time ticket in the URL fragment; <c>sso/exchange</c> trades the ticket for the
/// session; <c>sso/logout</c> ends the session and returns the provider's end_session URL. Every answer is no-store;
/// everything is under the per-client rate limit; 404 while <c>sso</c> is not in <c>Auth:Modes</c>.
/// </summary>
[ApiController]
[EnableRateLimiting(ClientRateLimitOptions.PolicyName)]
[RequiresLoginMode(SignInMode.Sso)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route(AuthRoutes.Base)]
public sealed class SsoController(
    BeginSsoSignIn begin,
    CompleteSsoSignIn complete,
    ExchangeSsoTicket exchange,
    SsoLogoutUseCase logout) : ControllerBase
{
    /// <summary>Starts a sign-in: 302 to the identity provider.</summary>
    [AllowAnonymous]
    [HttpGet(LoginModeRoutes.SsoLogin)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Login(
        [FromQuery(Name = LoginModeRoutes.ReturnUrlParameter)] string? returnUrl,
        [FromQuery(Name = LoginModeRoutes.BindingParameter)] string? binding)
    {
        var started = begin.Execute(returnUrl: returnUrl, bindingHash: binding);
        if (!started.IsSuccess)
        {
            return started.Refusal.ToActionResult();
        }

        Response.Cookies.Append(
            key: SsoPendingCookie.NameFor(started.Value.State), value: started.Value.SealedPending, SsoPendingCookie.Options(Request.IsHttps));
        return Redirect(started.Value.AuthorizationUrl.AbsoluteUri);
    }

    /// <summary>The provider's redirect back: 302 to the SPA callback with <c>#ticket=</c> or <c>#error=</c>.</summary>
    [AllowAnonymous]
    [HttpGet(LoginModeRoutes.SsoCallback)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> CallbackAsync(
        [FromQuery(Name = LoginModeRoutes.CodeParameter)] string? code,
        [FromQuery(Name = LoginModeRoutes.StateParameter)] string? state,
        [FromQuery(Name = LoginModeRoutes.ErrorParameter)] string? error,
        CancellationToken cancellationToken)
    {
        var cookieName = state is null ? null : SsoPendingCookie.NameFor(state);
        var pending = cookieName is null ? null : Request.Cookies[cookieName];
        if (cookieName is not null)
        {
            Response.Cookies.Delete(cookieName, SsoPendingCookie.Options(Request.IsHttps));
        }

        var redirect = await complete.ExecuteAsync(new SsoCallback(Code: code, State: state, ProviderError: error, SealedPending: pending), cancellationToken);
        return Redirect(redirect.Location.AbsoluteUri);
    }

    /// <summary>Trades the one-time ticket for the session (the same body as <c>/api/auth/login</c>).</summary>
    [AllowAnonymous]
    [HttpPost(LoginModeRoutes.SsoExchange)]
    [Produces(AuthRoutes.JsonContentType)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SessionResponse>> ExchangeAsync([FromBody] SsoExchangeCommand command, CancellationToken cancellationToken) =>
        LoginModeResults.ToActionResult(await exchange.ExecuteAsync(command, cancellationToken), LoginModeResults.Session);

    /// <summary>Ends the caller's session: 200 with the provider's end_session URL for an SSO session, else 204.</summary>
    [AuthenticatedUser]
    [HttpPost(LoginModeRoutes.SsoLogout)]
    [Produces(AuthRoutes.JsonContentType)]
    [ProducesResponseType<SsoLogoutResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SsoLogoutResponse>> LogoutAsync(CancellationToken cancellationToken)
    {
        var outcome = await logout.ExecuteAsync(cancellationToken);
        if (outcome is { IsSuccess: true, Value.EndSessionUrl: null })
        {
            return NoContent();
        }

        return outcome.ToActionResult(result => new SsoLogoutResponse(result.EndSessionUrl!.AbsoluteUri));
    }
}

/// <summary>The login methods the login page offers.</summary>
/// <param name="Basic">User name and password.</param>
/// <param name="ActiveDirectory">Directory user name and password.</param>
/// <param name="Sso">The organization's identity provider.</param>
public sealed record LoginModesResponse(bool Basic, bool ActiveDirectory, bool Sso)
{
    /// <summary>The response of <paramref name="modes"/>.</summary>
    public static LoginModesResponse From(EnabledLoginModes modes)
    {
        ArgumentNullException.ThrowIfNull(modes);
        return new LoginModesResponse(Basic: modes.Basic, ActiveDirectory: modes.ActiveDirectory, Sso: modes.Sso);
    }
}

/// <summary>The end of an SSO logout.</summary>
/// <param name="EndSessionUrl">The identity provider's end_session URL; open it with a top-level GET (not fetch).</param>
public sealed record SsoLogoutResponse(string EndSessionUrl);

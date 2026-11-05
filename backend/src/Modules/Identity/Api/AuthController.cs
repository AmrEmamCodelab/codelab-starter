using Codelab.Security.PublicIds;
using Codelab.Security.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Hosting;
using Starter.Infrastructure.Results;
using Starter.Modules.Identity.Application;

namespace Starter.Modules.Identity.Api;

/// <summary>
/// Sign in, refresh and sign out. Thin: each action calls one use case and maps its outcome. Every action is under the
/// per-client rate limit (<see cref="ClientRateLimitOptions.PolicyName"/>, <c>Codelab:Security:RateLimiting</c>), so
/// passwords and refresh tokens cannot be guessed without limit; a 429 problem answers past the limit.
/// </summary>
[ApiController]
[EnableRateLimiting(ClientRateLimitOptions.PolicyName)]
[Route(AuthRoutes.Base)]
[Produces(AuthRoutes.JsonContentType)]
public sealed class AuthController(LoginUseCase login, RefreshSessionUseCase refresh, LogoutUseCase logout) : ControllerBase
{
    /// <summary>Signs in with a user name and password.</summary>
    [AllowAnonymous]
    [HttpPost(AuthRoutes.Login)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SessionResponse>> LoginAsync(
        [FromBody] LoginCommand command, CancellationToken cancellationToken) =>
        (await login.ExecuteAsync(command, cancellationToken)).ToActionResult(SessionResponse.From);

    /// <summary>Rotates the refresh token and issues a new access token.</summary>
    [AllowAnonymous]
    [HttpPost(AuthRoutes.Refresh)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SessionResponse>> RefreshAsync(
        [FromBody] RefreshCommand command, CancellationToken cancellationToken) =>
        (await refresh.ExecuteAsync(command, cancellationToken)).ToActionResult(SessionResponse.From);

    /// <summary>Ends the caller's session; the access token in hand is refused from the next request on.</summary>
    [AuthenticatedUser]
    [HttpPost(AuthRoutes.Logout)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> LogoutAsync(CancellationToken cancellationToken) =>
        (await logout.ExecuteAsync(cancellationToken)).ToNoContentResult();
}

/// <summary>The Identity routes, built from <see cref="ApiRoutes.Prefix"/>.</summary>
public static class AuthRoutes
{
    /// <summary>The controller route: <c>api/auth</c>.</summary>
    public const string Base = ApiRoutes.Prefix + ApiRoutes.Separator + "auth";

    /// <summary><c>POST api/auth/login</c>.</summary>
    public const string Login = "login";

    /// <summary><c>POST api/auth/refresh</c>.</summary>
    public const string Refresh = "refresh";

    /// <summary><c>POST api/auth/logout</c>.</summary>
    public const string Logout = "logout";

    /// <summary><c>POST api/auth/activate</c> (anonymous: redeems an activation link).</summary>
    public const string Activate = "activate";

    /// <summary>The response content type.</summary>
    public const string JsonContentType = "application/json";
}

/// <summary>The family of user ids on the wire (<c>PublicId&lt;Users&gt;</c>).</summary>
public sealed class Users : IIdDomain
{
    /// <summary>The domain label bound into every user PublicId (part of the wire format; never rename).</summary>
    public const string DomainLabel = "users";

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}

/// <summary>A signed-in user as the client sees it.</summary>
/// <param name="Id">The user's PublicId.</param>
/// <param name="DisplayName">The display name.</param>
public sealed record SessionUserResponse([property: WireId] PublicId<Users> Id, string DisplayName);

/// <summary>The body of a successful login or refresh.</summary>
/// <param name="AccessToken">The bearer access token.</param>
/// <param name="RefreshToken">The refresh token (single use: every refresh returns a new one).</param>
/// <param name="AccessTokenExpiresAt">When the access token expires.</param>
/// <param name="IdleTimeoutMinutes">The idle timeout the client enforces, in minutes.</param>
/// <param name="User">The signed-in user.</param>
public sealed record SessionResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    int IdleTimeoutMinutes,
    SessionUserResponse User)
{
    /// <summary>The response of <paramref name="result"/>.</summary>
    public static SessionResponse From(LoginResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new SessionResponse(
            AccessToken: result.AccessToken,
            RefreshToken: result.RefreshToken,
            AccessTokenExpiresAt: result.AccessTokenExpiresAt,
            IdleTimeoutMinutes: result.IdleTimeoutMinutes,
            User: new SessionUserResponse(Id: PublicId<Users>.From(result.UserId), DisplayName: result.DisplayName));
    }
}

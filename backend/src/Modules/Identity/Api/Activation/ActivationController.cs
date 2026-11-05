using Codelab.Security.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Starter.Infrastructure.Results;
using Starter.Modules.Identity.Application.Activation;

namespace Starter.Modules.Identity.Api.Activation;

/// <summary>
/// Redeems an activation link: anonymous (the sealed one-time ticket is the proof) and under the per-client rate limit,
/// so tickets and passwords cannot be tried without limit. Thin: calls one use case and maps its outcome.
/// </summary>
[ApiController]
[EnableRateLimiting(ClientRateLimitOptions.PolicyName)]
[Route(AuthRoutes.Base)]
[Produces(AuthRoutes.JsonContentType)]
public sealed class ActivationController(ActivateAccountUseCase activate) : ControllerBase
{
    /// <summary>Sets the account's password from an activation link; the link is spent.</summary>
    [AllowAnonymous]
    [HttpPost(AuthRoutes.Activate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> ActivateAsync([FromBody] ActivateAccountCommand command, CancellationToken cancellationToken) =>
        (await activate.ExecuteAsync(command, cancellationToken)).ToNoContentResult();
}

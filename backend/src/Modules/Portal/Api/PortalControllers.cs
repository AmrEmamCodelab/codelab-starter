using Codelab.Security.RateLimiting;
using Codelab.Security.Signing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Results;
using Starter.Modules.Portal.Application;
using Starter.Modules.Portal.Application.Contact;
using Starter.Modules.Portal.Application.Content;

namespace Starter.Modules.Portal.Api;

/// <summary>Why the public portal endpoints are exempt from request signing (one owner of the reason).</summary>
public static class PortalSigning
{
    /// <summary>
    /// The reason: the public pages run before and without a session, so the caller holds no signing key; the same
    /// call must behave alike from a signed-in tab. The endpoints read nothing per user.
    /// </summary>
    public const string AnonymousReason =
        "A public portal call made before and without a session: the caller holds no signing key, and the same call "
        + "must behave alike from a signed-in tab. It reads nothing per user.";
}

/// <summary>
/// The public contact form: anonymous (the people most likely to write have no account) and under the per-client rate
/// limit (<see cref="ClientRateLimitOptions.PolicyName"/>, trusted-proxy aware), since each call stores a row. Thin:
/// calls one use case and maps its outcome.
/// </summary>
[ApiController]
[Route(PortalRoutes.Root)]
[Produces(PortalRoutes.JsonContentType)]
[EnableRateLimiting(ClientRateLimitOptions.PolicyName)]
public sealed class PortalContactController(SubmitContactMessage submit) : ControllerBase
{
    /// <summary>
    /// Stores a contact message and answers its reference number (201), the field errors (400), 409 while an
    /// administrator has switched the form off, or 429.
    /// </summary>
    [AllowAnonymous]
    [SignatureExempt(SignatureExemption.WholeRequest, PortalSigning.AnonymousReason)]
    [HttpPost(PortalRoutes.ContactMessages)]
    [ProducesResponseType<ContactReceiptResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ContactReceiptResponse>> SubmitAsync([FromBody] SubmitContactMessageCommand command, CancellationToken cancellationToken) =>
        (await submit.ExecuteAsync(command, cancellationToken)).ToCreatedResult(ContactReceiptResponse.From);
}

/// <summary>The public content pages (FAQ, service guide, privacy, terms): anonymous and served from a per-node cache.</summary>
[ApiController]
[Route(PortalRoutes.Root)]
[Produces(PortalRoutes.JsonContentType)]
public sealed class PortalContentController(GetPublishedPortalContent getContent) : ControllerBase
{
    /// <summary>One page's content; 404 for an unknown page key.</summary>
    [AllowAnonymous]
    [SignatureExempt(SignatureExemption.WholeRequest, PortalSigning.AnonymousReason)]
    [HttpGet(PortalRoutes.Content)]
    [ProducesResponseType<PortalContentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortalContentResponse>> GetAsync(string page, CancellationToken cancellationToken) =>
        (await getContent.ExecuteAsync(page, cancellationToken)).ToActionResult(PortalContentResponse.From);
}

/// <summary>The administrator's content editor: read the current sections, replace them.</summary>
[ApiController]
[Route(PortalRoutes.Admin)]
[Produces(PortalRoutes.JsonContentType)]
public sealed class AdminPortalContentController(GetPortalContent getContent, ReplacePortalContent replaceContent) : ControllerBase
{
    /// <summary>One page's current content (never cached); 404 for an unknown page key.</summary>
    [HttpGet(PortalRoutes.Content)]
    [HasPermission(PortalPermissionKeys.ContentView)]
    [ProducesResponseType<PortalContentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortalContentResponse>> GetAsync(string page, CancellationToken cancellationToken) =>
        (await getContent.ExecuteAsync(page, cancellationToken)).ToActionResult(PortalContentResponse.From);

    /// <summary>Replaces one page's sections and answers the page as stored; 400, 404 or 409 otherwise.</summary>
    [HttpPut(PortalRoutes.Content)]
    [HasPermission(PortalPermissionKeys.ContentManage)]
    [ProducesResponseType<PortalContentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PortalContentResponse>> ReplaceAsync(
        string page, [FromBody] ReplacePortalContentCommand command, CancellationToken cancellationToken) =>
        (await replaceContent.ExecuteAsync(page, command, cancellationToken)).ToActionResult(PortalContentResponse.From);
}

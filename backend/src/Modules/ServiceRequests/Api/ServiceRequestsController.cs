using Codelab.Security.PublicIds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Application.Results;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Results;
using Starter.Modules.ServiceRequests.Application;
using Starter.Modules.ServiceRequests.Application.Drafts;

namespace Starter.Modules.ServiceRequests.Api;

/// <summary>
/// The typed wizard endpoints of the sample: create a draft, resume and save it step by step, submit it, and read its
/// fields for the detail cards. Resubmit, cancel and the staff decisions go through the generic record transitions
/// endpoint (module Records). Thin: each action calls one use case and maps its outcome.
/// </summary>
[ApiController]
[Route(ServiceRequestsRoutes.Root)]
[Produces(ServiceRequestsRoutes.JsonContentType)]
public sealed class ServiceRequestsController(
    CreateServiceRequestDraft createDraft,
    GetServiceRequestDraft getDraft,
    SaveServiceRequestDraft saveDraft,
    SubmitServiceRequest submit,
    GetServiceRequestDetails getDetails) : ControllerBase
{
    /// <summary>Creates an empty draft owned by the caller: 201 <c>{ id }</c>.</summary>
    [HttpPost]
    [HasPermission(ServiceRequestsPermissionKeys.Create)]
    [ProducesResponseType<CreatedServiceRequestResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CreatedServiceRequestResponse>> CreateAsync(CancellationToken cancellationToken) =>
        (await createDraft.ExecuteAsync(cancellationToken)).ToCreatedResult(CreatedServiceRequestResponse.From);

    /// <summary>The caller's draft for the wizard to resume; 404 when missing or not the caller's.</summary>
    [HttpGet(ServiceRequestsRoutes.Draft)]
    [HasPermission(ServiceRequestsPermissionKeys.Create)]
    [ProducesResponseType<ServiceRequestDraftResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceRequestDraftResponse>> GetDraftAsync(PublicId<ServiceRequestRecordIds> id, CancellationToken cancellationToken) =>
        (await getDraft.ExecuteAsync(id.Value, cancellationToken)).ToActionResult(ServiceRequestDraftResponse.From);

    /// <summary>Saves one wizard step: 204; 400 with field errors; 404; 409 once the request is not editable.</summary>
    [HttpPut(ServiceRequestsRoutes.Draft)]
    [HasPermission(ServiceRequestsPermissionKeys.Create)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> SaveDraftAsync(
        PublicId<ServiceRequestRecordIds> id, [FromBody] SaveServiceRequestDraftCommand command, CancellationToken cancellationToken) =>
        (await saveDraft.ExecuteAsync(id.Value, command, cancellationToken)).ToNoContentResult();

    /// <summary>
    /// Submits the draft: 200 <c>{ number }</c>; 400 <c>VALIDATION_FAILED</c> with the field errors of every incomplete
    /// step; 404; 409 <c>ATTACHMENTS_REQUIRED_MISSING</c> or <c>RECORDS_NOT_SUBMITTABLE</c>.
    /// </summary>
    [HttpPost(ServiceRequestsRoutes.Submit)]
    [HasPermission(ServiceRequestsPermissionKeys.Create)]
    [ProducesResponseType<SubmittedServiceRequestResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SubmittedServiceRequestResponse>> SubmitAsync(PublicId<ServiceRequestRecordIds> id, CancellationToken cancellationToken) =>
        (await submit.ExecuteAsync(id.Value, cancellationToken)).ToActionResult(SubmittedServiceRequestResponse.From);

    /// <summary>
    /// The request's fields for the detail cards. Signed-in users only; the use case lets through the owner (with
    /// <c>service-requests.view-own</c>) and queue holders whose scope reaches it, and answers 404 to anyone else.
    /// </summary>
    [HttpGet(ServiceRequestsRoutes.IdTemplate)]
    [AuthenticatedUser]
    [ProducesResponseType<ServiceRequestDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceRequestDetailsResponse>> GetAsync(PublicId<ServiceRequestRecordIds> id, CancellationToken cancellationToken) =>
        (await getDetails.ExecuteAsync(id.Value, cancellationToken)).ToActionResult(ServiceRequestDetailsResponse.From);
}

/// <summary>The 201 mapping of the create endpoint (the kernel's <c>OutcomeResults</c> answers 200).</summary>
public static class ServiceRequestsResults
{
    /// <summary>201 with the mapped value, or the refusal's problem+json.</summary>
    public static ActionResult<TResponse> ToCreatedResult<T, TResponse>(this Outcome<T> outcome, Func<T, TResponse> map)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(map);
        return outcome.IsSuccess
            ? new ObjectResult(map(outcome.Value)) { StatusCode = StatusCodes.Status201Created }
            : outcome.Refusal.ToActionResult();
    }
}

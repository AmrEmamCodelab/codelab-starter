using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Infrastructure.Results;
using Starter.Modules.Ops.Application.Certificates;
using Starter.Modules.Ops.Application.Health;
using Starter.Modules.Ops.Application.Jobs;
using Starter.Modules.Ops.Application.ReferenceCodes;
using Starter.Modules.Ops.Domain;

namespace Starter.Modules.Ops.Api;

/// <summary>
/// The Ops surface, called only by the Ops Console API with an ES256 service assertion (<see cref="OpsServiceCallerAttribute"/>);
/// every call is recorded with its operator by <see cref="OperatorAccessFilter"/>. Thin: each action calls one use
/// case and maps its outcome. The front site answers 404 for <c>/api/ops/*</c>; only the console reaches it.
/// </summary>
[ApiController]
[Route(OpsRoutes.Root)]
[Produces(OpsRoutes.JsonContentType)]
[OpsServiceCaller]
[ServiceFilter<OperatorAccessFilter>]
public sealed class OpsController(
    GetSystemHealth getSystemHealth,
    ListJobStatuses listJobStatuses,
    ListCertificates listCertificates,
    FindReferenceCode findReferenceCode) : ControllerBase
{
    /// <summary>The health of the database, the attachment service and the gateway, and the system status.</summary>
    [HttpGet(OpsRoutes.Health)]
    [OpsEndpoint(OpsEndpoint.Health)]
    [ProducesResponseType<SystemHealthResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemHealthResponse>> GetHealthAsync(CancellationToken cancellationToken) =>
        (await getSystemHealth.ExecuteAsync(cancellationToken)).ToActionResult(SystemHealthResponse.From);

    /// <summary>Every registered job with its schedule and newest run.</summary>
    [HttpGet(OpsRoutes.Jobs)]
    [OpsEndpoint(OpsEndpoint.Jobs)]
    [ProducesResponseType<IReadOnlyList<OpsJobResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OpsJobResponse>>> ListJobsAsync(CancellationToken cancellationToken) =>
        (await listJobStatuses.ExecuteAsync(cancellationToken)).ToActionResult(OpsJobResponse.ListFrom);

    /// <summary>The watched certificates with days left and status, the most urgent first.</summary>
    [HttpGet(OpsRoutes.Certificates)]
    [OpsEndpoint(OpsEndpoint.Certificates)]
    [ProducesResponseType<IReadOnlyList<CertificateResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CertificateResponse>>> ListCertificatesAsync(CancellationToken cancellationToken) =>
        (await listCertificates.ExecuteAsync(cancellationToken)).ToActionResult(CertificateResponse.ListFrom);

    /// <summary>The request behind a reference code and the window to search the log in.</summary>
    [HttpGet(OpsRoutes.ReferenceCode)]
    [OpsEndpoint(OpsEndpoint.ReferenceCode)]
    [ProducesResponseType<ReferenceCodeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReferenceCodeResponse>> FindReferenceCodeAsync(
        [FromRoute(Name = OpsRoutes.CodeParameter)] string code,
        CancellationToken cancellationToken) =>
        (await findReferenceCode.ExecuteAsync(code, cancellationToken)).ToActionResult(ReferenceCodeResponse.From);
}

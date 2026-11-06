using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Results;
using Starter.Modules.Jobs.Application;
using Starter.Modules.Jobs.Application.Admin;

namespace Starter.Modules.Jobs.Api;

/// <summary>The background jobs admin screen. Thin: each action calls one use case and maps its outcome.</summary>
[ApiController]
[Route(JobsRoutes.Admin)]
[Produces(JobsRoutes.JsonContentType)]
public sealed class AdminJobsController(ListJobs listJobs, TriggerJobRun triggerJobRun, ListJobRuns listJobRuns) : ControllerBase
{
    /// <summary>Every registered job with its newest run, ordered by key.</summary>
    [HttpGet]
    [HasPermission(JobsPermissionKeys.View)]
    [ProducesResponseType<IReadOnlyList<JobResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<JobResponse>>> ListAsync(CancellationToken cancellationToken) =>
        (await listJobs.ExecuteAsync(cancellationToken)).ToActionResult(JobResponse.ListFrom);

    /// <summary>Queues a run of the job now; a job server runs it as the system actor.</summary>
    [HttpPost(JobsRoutes.Runs)]
    [HasPermission(JobsPermissionKeys.Run)]
    [ProducesResponseType<TriggeredJobRunResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TriggeredJobRunResponse>> TriggerAsync(
        [FromRoute(Name = JobsRoutes.KeyParameter)] string key,
        CancellationToken cancellationToken) =>
        (await triggerJobRun.ExecuteAsync(key, cancellationToken)).ToAcceptedResult(TriggeredJobRunResponse.From);

    /// <summary>The newest runs of the job, newest first.</summary>
    [HttpGet(JobsRoutes.Runs)]
    [HasPermission(JobsPermissionKeys.View)]
    [ProducesResponseType<IReadOnlyList<JobRunResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<JobRunResponse>>> ListRunsAsync(
        [FromRoute(Name = JobsRoutes.KeyParameter)] string key,
        [FromQuery] JobRunsQuery query,
        CancellationToken cancellationToken) =>
        (await listJobRuns.ExecuteAsync(key, query, cancellationToken)).ToActionResult(JobRunResponse.ListFrom);
}

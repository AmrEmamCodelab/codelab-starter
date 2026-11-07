using Starter.Application.Jobs;
using Starter.Modules.Ops.Application;
using Starter.Modules.Ops.Application.Certificates;
using Starter.Modules.Ops.Application.Health;
using Starter.Modules.Ops.Application.ReferenceCodes;
using Starter.Modules.Ops.Domain.Certificates;
using Starter.Modules.Ops.Domain.Health;

namespace Starter.Modules.Ops.Api;

/// <summary>The health summary: the worst component status as the system status.</summary>
/// <param name="Status">The system status (Healthy = 1, Degraded = 2, Unhealthy = 3).</param>
/// <param name="CheckedAt">When the checks started (ISO 8601 UTC).</param>
/// <param name="Components">Each component, ordered by name.</param>
public sealed record SystemHealthResponse(ComponentStatus Status, DateTimeOffset CheckedAt, IReadOnlyList<ComponentHealthResponse> Components)
{
    /// <summary>The response of <paramref name="health"/>.</summary>
    public static SystemHealthResponse From(SystemHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);
        return new SystemHealthResponse(
            health.Status,
            health.CheckedAt,
            [.. health.Components.Select(check => new ComponentHealthResponse(check.Name, check.Status, check.DurationMs, check.FindingCode))]);
    }
}

/// <summary>One component of the health summary.</summary>
/// <param name="Name">The component name.</param>
/// <param name="Status">Its status.</param>
/// <param name="DurationMs">How long its check took, in milliseconds.</param>
/// <param name="Code">Why it is not healthy (an <c>OPS_*</c> finding code), or null.</param>
public sealed record ComponentHealthResponse(string Name, ComponentStatus Status, long DurationMs, string? Code);

/// <summary>One job and its newest run.</summary>
/// <param name="Key">The job key.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="NameEn">The English name.</param>
/// <param name="Cron">The schedule (five-field cron in the jobs time zone), or null.</param>
/// <param name="LastRun">The newest run, or null.</param>
public sealed record OpsJobResponse(string Key, string NameAr, string NameEn, string? Cron, OpsJobRunResponse? LastRun)
{
    /// <summary>The responses of <paramref name="jobs"/>.</summary>
    public static IReadOnlyList<OpsJobResponse> ListFrom(IReadOnlyList<JobStatus> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        return [.. jobs.Select(job => new OpsJobResponse(job.Key, job.NameAr, job.NameEn, job.Cron, OpsJobRunResponse.From(job.LastRun)))];
    }
}

/// <summary>The newest run of a job.</summary>
/// <param name="Status">The run state (Queued = 1, Running = 2, Succeeded = 3, Failed = 4).</param>
/// <param name="Trigger">Why it started (Schedule = 1, Manual = 2).</param>
/// <param name="StartedAt">When it started, or null.</param>
/// <param name="FinishedAt">When it ended, or null.</param>
/// <param name="ReferenceCode">The reference code of a failed run, or null.</param>
public sealed record OpsJobRunResponse(JobRunStatus Status, JobTrigger Trigger, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? ReferenceCode)
{
    /// <summary>The response of <paramref name="run"/>, or null.</summary>
    public static OpsJobRunResponse? From(JobLastRun? run) =>
        run is null ? null : new OpsJobRunResponse(run.Status, run.Trigger, run.StartedAt, run.FinishedAt, run.ReferenceCode);
}

/// <summary>One certificate and how close it is to its end.</summary>
/// <param name="Name">The row name.</param>
/// <param name="Subject">The subject, or null when unreadable.</param>
/// <param name="NotAfter">The end date (ISO 8601 UTC), or null when unreadable.</param>
/// <param name="DaysLeft">Whole days left, or null when unreadable.</param>
/// <param name="Status">Ok = 1, Warning = 2, Critical = 3, Expired = 4 (an unreadable certificate is Critical).</param>
/// <param name="Code">Why it could not be read (an <c>OPS_*</c> finding code), or null.</param>
public sealed record CertificateResponse(string Name, string? Subject, DateTimeOffset? NotAfter, int? DaysLeft, CertificateStatus Status, string? Code)
{
    /// <summary>The responses of <paramref name="reports"/>.</summary>
    public static IReadOnlyList<CertificateResponse> ListFrom(IReadOnlyList<CertificateReport> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        return [.. reports.Select(report => new CertificateResponse(report.Name, report.Subject, report.NotAfter, report.DaysLeft, report.Status, report.FindingCode))];
    }
}

/// <summary>Where to find the request behind a reference code.</summary>
/// <param name="ReferenceCode">The code, uppercase.</param>
/// <param name="CorrelationId">The full correlation id (the <c>CorrelationId</c> property of every log line of the request).</param>
/// <param name="TraceId">The W3C trace id, or null.</param>
/// <param name="FirstSeen">When the error response completed.</param>
/// <param name="Request">The request line and status.</param>
/// <param name="SearchFrom">The start of the log search window.</param>
/// <param name="SearchTo">The end of the log search window.</param>
public sealed record ReferenceCodeResponse(
    string ReferenceCode,
    string CorrelationId,
    string? TraceId,
    DateTimeOffset FirstSeen,
    ReferenceCodeRequestResponse Request,
    DateTimeOffset SearchFrom,
    DateTimeOffset SearchTo)
{
    /// <summary>The response of <paramref name="trace"/>.</summary>
    public static ReferenceCodeResponse From(ReferenceCodeTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        return new ReferenceCodeResponse(
            trace.ReferenceCode,
            trace.CorrelationId,
            trace.TraceId,
            trace.FirstSeen,
            new ReferenceCodeRequestResponse(trace.Method, trace.Path, trace.StatusCode),
            trace.SearchFrom,
            trace.SearchTo);
    }
}

/// <summary>The request behind a reference code.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path, without the query string.</param>
/// <param name="Status">The response status code.</param>
public sealed record ReferenceCodeRequestResponse(string Method, string Path, int Status);

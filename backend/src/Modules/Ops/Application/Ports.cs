using Starter.Application.Jobs;
using Starter.Modules.Ops.Domain;
using Starter.Modules.Ops.Domain.Certificates;
using Starter.Modules.Ops.Domain.Health;

namespace Starter.Modules.Ops.Application;

/// <summary>
/// One component the health summary checks (the database, a readiness URL). <see cref="CheckAsync"/> never throws for
/// an unhealthy component: it answers <see cref="ComponentStatus.Unhealthy"/> with a finding code. It honours the
/// cancellation token and bounds its own time by the probe timeout.
/// </summary>
public interface IComponentProbe
{
    /// <summary>The component's stable name (<c>database</c>, <c>attachment-service</c>, <c>integration-gateway</c>).</summary>
    string Name { get; }

    /// <summary>Checks the component once.</summary>
    Task<ComponentCheck> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>The answer of one <see cref="IComponentProbe"/>.</summary>
/// <param name="Name">The component name.</param>
/// <param name="Status">Its status.</param>
/// <param name="DurationMs">How long the check took, in milliseconds.</param>
/// <param name="FindingCode">Why it is not healthy (<see cref="OpsFindingCodes"/>), or null.</param>
public sealed record ComponentCheck(string Name, ComponentStatus Status, long DurationMs, string? FindingCode);

/// <summary>The job catalog with the newest run of each job.</summary>
public interface IJobStatusSource
{
    /// <summary>Every registered job with its newest run (null when it never ran, or when no run history is reachable).</summary>
    Task<IReadOnlyList<JobStatus>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>One job and its newest run.</summary>
/// <param name="Key">The job key.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="NameEn">The English name.</param>
/// <param name="Cron">The schedule, or null for a manual-only job.</param>
/// <param name="LastRun">The newest run, or null.</param>
public sealed record JobStatus(string Key, string NameAr, string NameEn, string? Cron, JobLastRun? LastRun);

/// <summary>The newest run of a job.</summary>
/// <param name="Status">Its state.</param>
/// <param name="Trigger">Why it started.</param>
/// <param name="StartedAt">When it started executing, or null while queued.</param>
/// <param name="FinishedAt">When it ended, or null.</param>
/// <param name="ReferenceCode">The reference code of a failed run, or null.</param>
public sealed record JobLastRun(JobRunStatus Status, JobTrigger Trigger, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? ReferenceCode);

/// <summary>
/// A source of certificates whose end dates operators watch (the configured TLS and signing certificates, the identity
/// provider's signing certificates). <see cref="ReadAsync"/> never throws for an unreadable certificate: it returns a
/// fact with a finding code and no end date.
/// </summary>
public interface ICertificateSource
{
    /// <summary>The certificates of this source.</summary>
    Task<IReadOnlyList<CertificateFact>> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>One certificate as read, before it is judged.</summary>
/// <param name="Name">The configured name (or <c>idp-signing-N</c>).</param>
/// <param name="Subject">The certificate subject, or null when it could not be read.</param>
/// <param name="NotAfter">The end date, or null when it could not be read.</param>
/// <param name="FindingCode">Why it could not be read (<see cref="OpsFindingCodes"/>), or null.</param>
public sealed record CertificateFact(string Name, string? Subject, DateTimeOffset? NotAfter, string? FindingCode);

/// <summary>The reference-code index (<c>ops.ReferenceCodeSightings</c>).</summary>
public interface IReferenceCodeSightings
{
    /// <summary>The newest sighting of <paramref name="referenceCode"/> (uppercase), or null.</summary>
    Task<ReferenceCodeSighting?> FindNewestAsync(string referenceCode, CancellationToken cancellationToken);

    /// <summary>Stores <paramref name="sightings"/>.</summary>
    Task AddAsync(IReadOnlyCollection<ReferenceCodeSighting> sightings, CancellationToken cancellationToken);

    /// <summary>Deletes every sighting older than <paramref name="cutoff"/>; returns how many.</summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}

/// <summary>
/// The Ops audit trail (<c>ops.OperatorAccess</c>). <see cref="RecordAsync"/> is best effort by contract: the Ops
/// endpoints are read-only diagnostics that must still answer while the database is down, so a failed write is logged
/// (with the endpoint and the correlation id) and the call goes on.
/// </summary>
public interface IOperatorAccessLog
{
    /// <summary>Records <paramref name="access"/>.</summary>
    Task RecordAsync(OperatorAccess access, CancellationToken cancellationToken);
}

/// <summary>The Ops settings the use cases read (built from <c>Codelab:Ops</c> by the module registration).</summary>
/// <param name="Thresholds">The certificate thresholds.</param>
/// <param name="SearchMargin">How far before and after a sighting the log search window reaches.</param>
/// <param name="SightingRetention">How long a reference-code sighting is kept.</param>
public sealed record OpsSettings(CertificateThresholds Thresholds, TimeSpan SearchMargin, TimeSpan SightingRetention);

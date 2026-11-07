using Starter.Application.Jobs;

namespace Starter.Modules.Ops.Application.ReferenceCodes;

/// <summary>The Ops job keys and schedules.</summary>
public static class OpsJobs
{
    /// <summary>The key of the reference-code purge job.</summary>
    public const string ReferenceCodePurge = "ops-reference-code-purge";

    /// <summary>Every day at 02:30 in the jobs time zone (after the security replay purge at 02:00).</summary>
    public const string DailyAtTwoThirty = "30 2 * * *";
}

/// <summary>
/// Deletes reference-code sightings older than <see cref="OpsSettings.SightingRetention"/>, once a day, so the index
/// stays as long as the log retention and no longer. Safe to run again: it deletes by age only.
/// </summary>
public sealed class ReferenceCodePurgeJob(IReferenceCodeSightings sightings, OpsSettings settings, TimeProvider clock) : IJob
{
    /// <summary>The Arabic name.</summary>
    public const string ArabicName = "حذف رموز المراجعة القديمة";

    /// <summary>The English name.</summary>
    public const string EnglishName = "Purge old reference codes";

    /// <inheritdoc />
    public string Key => OpsJobs.ReferenceCodePurge;

    /// <inheritdoc />
    public string NameAr => ArabicName;

    /// <inheritdoc />
    public string NameEn => EnglishName;

    /// <inheritdoc />
    public string? Cron => OpsJobs.DailyAtTwoThirty;

    /// <inheritdoc />
    public Task RunAsync(JobRunContext context, CancellationToken cancellationToken) =>
        sightings.PurgeOlderThanAsync(clock.GetUtcNow() - settings.SightingRetention, cancellationToken);
}

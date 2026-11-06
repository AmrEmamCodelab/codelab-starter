using Codelab.Security.Storage;
using Microsoft.Extensions.Logging;
using Starter.Application.Jobs;
using Starter.Modules.Jobs.Application.BuiltIn;

namespace Starter.Modules.Jobs.Infrastructure.BuiltIn;

/// <summary>
/// <c>security-replay-purge</c>, daily at 02:00: Codelab.Security's purge hook (<see cref="SecurityStorePurgeJob"/>),
/// which deletes the expired rows of every store that keeps them (replay nonces, revoked session ids). Stores held in
/// memory purge nothing. A store that fails does not stop the others; the run fails afterwards.
/// </summary>
public sealed partial class SecurityReplayPurgeJob(SecurityStorePurgeJob purge, ILogger<SecurityReplayPurgeJob> logger) : IJob
{
    /// <summary>The Arabic name.</summary>
    public const string ArabicName = "حذف سجلات الحماية المنتهية";

    /// <summary>The English name.</summary>
    public const string EnglishName = "Purge expired security records";

    /// <inheritdoc />
    public string Key => JobsJobKeys.SecurityReplayPurge;

    /// <inheritdoc />
    public string NameAr => ArabicName;

    /// <inheritdoc />
    public string NameEn => EnglishName;

    /// <inheritdoc />
    public string? Cron => JobsSchedules.DailyAtTwoAm;

    /// <inheritdoc />
    public async Task RunAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var removed = await purge.RunAsync(cancellationToken);
        foreach (var (store, count) in removed)
        {
            LogPurged(logger, context.RunId, store, count);
        }
    }

    [LoggerMessage(EventId = 6302, Level = LogLevel.Information, Message = "Security purge run {RunId}: {Count} expired row(s) removed from {Store}")]
    private static partial void LogPurged(ILogger logger, long runId, string store, int count);
}

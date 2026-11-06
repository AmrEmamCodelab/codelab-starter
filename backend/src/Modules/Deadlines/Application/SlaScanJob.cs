using Microsoft.Extensions.Logging;
using Starter.Application.Jobs;

namespace Starter.Modules.Deadlines.Application;

/// <summary>
/// <c>deadlines-sla-scan</c>, every 5 minutes, as the system actor: one <see cref="SlaScan"/> pass (mark the breaches,
/// escalate each once). A failed escalation fails the run after the other breaches were handled; the next tick retries
/// what was given back.
/// </summary>
public sealed partial class SlaScanJob(SlaScan scan, ILogger<SlaScanJob> logger) : IJob
{
    /// <summary>The Arabic name.</summary>
    public const string ArabicName = "مراقبة مهل مستوى الخدمة";

    /// <summary>The English name.</summary>
    public const string EnglishName = "Watch service-level deadlines";

    /// <inheritdoc />
    public string Key => DeadlinesJobKeys.SlaScan;

    /// <inheritdoc />
    public string NameAr => ArabicName;

    /// <inheritdoc />
    public string NameEn => EnglishName;

    /// <inheritdoc />
    public string? Cron => DeadlinesSchedules.EveryFiveMinutes;

    /// <inheritdoc />
    public async Task RunAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = await scan.RunAsync(cancellationToken);
        LogScanned(logger, context.RunId, result.Breached, result.Escalated);
        LogNotEscalated(logger, context.RunId, result.Unaddressed, result.Skipped, result.Closed);
    }

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information, Message = "SLA scan run {RunId}: {Breached} breached, {Escalated} escalated")]
    private static partial void LogScanned(ILogger logger, long runId, int breached, int escalated);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information, Message = "SLA scan run {RunId}: {Unaddressed} without recipient, {Skipped} without definition, {Closed} stale closed")]
    private static partial void LogNotEscalated(ILogger logger, long runId, int unaddressed, int skipped, int closed);
}

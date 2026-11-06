using Microsoft.Extensions.Logging;
using Starter.Application.Jobs;
using Starter.Application.Messaging;

namespace Starter.Modules.Jobs.Application.BuiltIn;

/// <summary>
/// <c>outbox-drain</c>, every 5 minutes: one pass of the kernel outbox drain (<see cref="IOutboxDrainRunner"/>), which
/// delivers the due messages to their handlers, retries with back-off and does nothing while
/// <c>Messaging:Outbox:Enabled</c> is false. A failed delivery is the drain's business (it stays in the outbox); only a
/// failure of the pass itself fails the run.
/// </summary>
public sealed partial class OutboxDrainJob(IOutboxDrainRunner drain, ILogger<OutboxDrainJob> logger) : IJob
{
    /// <summary>The Arabic name.</summary>
    public const string ArabicName = "إرسال رسائل صندوق الصادر";

    /// <summary>The English name.</summary>
    public const string EnglishName = "Send outbox messages";

    /// <inheritdoc />
    public string Key => JobsJobKeys.OutboxDrain;

    /// <inheritdoc />
    public string NameAr => ArabicName;

    /// <inheritdoc />
    public string NameEn => EnglishName;

    /// <inheritdoc />
    public string? Cron => JobsSchedules.EveryFiveMinutes;

    /// <inheritdoc />
    public async Task RunAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = await drain.DrainOnceAsync(cancellationToken);
        LogDrained(logger, context.RunId, result.Sent, result.Retried, result.Failed);
    }

    [LoggerMessage(EventId = 6301, Level = LogLevel.Information, Message = "Outbox drain run {RunId}: {Sent} sent, {Retried} retried, {Failed} failed")]
    private static partial void LogDrained(ILogger logger, long runId, int sent, int retried, int failed);
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Starter.Application.Messaging;

namespace Starter.Infrastructure.Messaging;

/// <summary>
/// The <see cref="IOutboxDrainRunner"/>: fails abandoned final attempts, then claims due messages in batches (at most
/// <see cref="OutboxDrainSchedule.MaxBatchesPerPass"/>), delivers each through <see cref="OutboxDispatcher"/> and records
/// the outcome: Sent; Pending again after <see cref="OutboxDrainSchedule.BackoffAfter"/>; or Failed when no handler
/// exists or the last of <see cref="OutboxLimits.MaxAttempts"/> attempts failed.
/// </summary>
/// <remarks>
/// A failed delivery is recorded against its own message, so one unreachable recipient does not stop the batch. A
/// store failure propagates: with the database unreachable there is nothing to record on, and the job shows red. The
/// outcome is recorded with <see cref="CancellationToken.None"/> once the handler returned, so a stopping host does not
/// turn a completed delivery into a redelivery.
/// </remarks>
public sealed partial class OutboxDrainRunner(
    IOutboxStore store,
    OutboxDispatcher dispatcher,
    IOptionsMonitor<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDrainRunner> logger) : IOutboxDrainRunner
{
    /// <inheritdoc />
    public async Task<OutboxDrainResult> DrainOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        if (!settings.Enabled)
        {
            LogDisabled(logger);
            return OutboxDrainResult.None;
        }

        var tally = new Tally { Failed = await store.FailAbandonedAsync(timeProvider.GetUtcNow(), cancellationToken) };
        for (var batch = 0; batch < OutboxDrainSchedule.MaxBatchesPerPass; batch++)
        {
            var claimed = await ClaimAsync(settings.BatchSize, cancellationToken);
            foreach (var delivery in claimed)
            {
                tally.Add(await DeliverAsync(delivery, cancellationToken));
            }

            if (claimed.Count < settings.BatchSize)
            {
                break;
            }
        }

        var result = tally.ToResult();
        LogPass(logger, result.Sent, result.Retried, result.Failed);
        return result;
    }

    private Task<IReadOnlyList<OutboxDelivery>> ClaimAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return store.ClaimAsync(new OutboxClaim(now, LockedUntil: now + OutboxDrainSchedule.Lease, batchSize), cancellationToken);
    }

    private async Task<OutboxMessageStatus> DeliverAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        var result = await dispatcher.DispatchAsync(delivery, cancellationToken);
        switch (result.Status)
        {
            case OutboxDispatchStatus.Delivered:
                await store.MarkSentAsync(delivery.MessageId, timeProvider.GetUtcNow(), CancellationToken.None);
                return OutboxMessageStatus.Sent;
            case OutboxDispatchStatus.HandlerFailed when delivery.Attempt < OutboxLimits.MaxAttempts:
                var notBefore = timeProvider.GetUtcNow() + OutboxDrainSchedule.BackoffAfter(delivery.Attempt);
                await store.MarkRetryAsync(new OutboxRetry(delivery.MessageId, delivery.Attempt, notBefore, result.ErrorCode!), CancellationToken.None);
                return OutboxMessageStatus.Pending;
            default:
                await store.MarkFailedAsync(new OutboxAttemptFailure(delivery.MessageId, delivery.Attempt, result.ErrorCode!), CancellationToken.None);
                LogGaveUp(logger, delivery.MessageId, delivery.Type, delivery.Attempt, result.ErrorCode!);
                return OutboxMessageStatus.Failed;
        }
    }

    [LoggerMessage(EventId = 1203, Level = LogLevel.Debug, Message = "Outbox drain skipped: Messaging:Outbox:Enabled is false")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Information, Message = "Outbox drain pass: {Sent} sent, {Retried} retried, {Failed} failed")]
    private static partial void LogPass(ILogger logger, int sent, int retried, int failed);

    [LoggerMessage(EventId = 1205, Level = LogLevel.Error, Message = "Outbox message {MessageId} ({MessageType}) failed for good after attempt {Attempt} with {ErrorCode}")]
    private static partial void LogGaveUp(ILogger logger, long messageId, string messageType, int attempt, string errorCode);

    /// <summary>The counts of one pass, by the state each message was left in.</summary>
    private sealed class Tally
    {
        public int Sent { get; private set; }

        public int Retried { get; private set; }

        public int Failed { get; set; }

        public void Add(OutboxMessageStatus outcome)
        {
            switch (outcome)
            {
                case OutboxMessageStatus.Sent:
                    Sent++;
                    break;
                case OutboxMessageStatus.Pending:
                    Retried++;
                    break;
                default:
                    Failed++;
                    break;
            }
        }

        public OutboxDrainResult ToResult() => new(Sent, Retried, Failed);
    }
}

using Starter.Application.Messaging;
using Starter.Infrastructure.Messaging;

namespace Starter.Api.Tests.Messaging;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>
/// An <see cref="IOutboxStore"/> in memory with the same predicates as the SQL in <see cref="SqlOutboxStore"/>: Pending
/// rows due by NotBefore, Processing rows whose lease expired with attempts left, attempts counted on claim, outcome
/// updates guarded by status and attempt.
/// </summary>
internal sealed class InMemoryOutboxStore : IOutboxStore
{
    private readonly List<Row> _rows = [];

    public int ClaimCalls { get; private set; }

    public IReadOnlyList<Row> Rows => _rows;

    public Row Add(string type, DateTimeOffset? notBefore = null, string payload = "{}")
    {
        var row = new Row { Id = _rows.Count + 1, Type = type, PayloadJson = payload, Status = OutboxMessageStatus.Pending, NotBefore = notBefore };
        _rows.Add(row);
        return row;
    }

    public Task<int> FailAbandonedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var abandoned = _rows.Where(row => row.Status == OutboxMessageStatus.Processing && row.LockedUntil <= now && row.Attempt >= OutboxLimits.MaxAttempts).ToList();
        abandoned.ForEach(row => row.Fail(OutboxErrorCodes.LeaseExpired));
        return Task.FromResult(abandoned.Count);
    }

    public Task<IReadOnlyList<OutboxDelivery>> ClaimAsync(OutboxClaim claim, CancellationToken cancellationToken)
    {
        ClaimCalls++;
        var due = _rows
            .Where(row => (row.Status == OutboxMessageStatus.Pending && (row.NotBefore is null || row.NotBefore <= claim.Now))
                || (row.Status == OutboxMessageStatus.Processing && row.LockedUntil <= claim.Now && row.Attempt < OutboxLimits.MaxAttempts))
            .OrderBy(row => row.Id)
            .Take(claim.BatchSize)
            .ToList();
        foreach (var row in due)
        {
            row.Status = OutboxMessageStatus.Processing;
            row.Attempt++;
            row.LockedUntil = claim.LockedUntil;
        }

        return Task.FromResult<IReadOnlyList<OutboxDelivery>>([.. due.Select(row => new OutboxDelivery(row.Id, row.Type, row.PayloadJson, row.Attempt))]);
    }

    public Task MarkSentAsync(long messageId, DateTimeOffset sentAt, CancellationToken cancellationToken)
    {
        if (Find(messageId) is { Status: OutboxMessageStatus.Processing } row)
        {
            row.Status = OutboxMessageStatus.Sent;
            row.SentAt = sentAt;
            row.LockedUntil = null;
            row.LastError = null;
        }

        return Task.CompletedTask;
    }

    public Task MarkRetryAsync(OutboxRetry retry, CancellationToken cancellationToken)
    {
        if (Held(retry.MessageId, retry.Attempt) is { } row)
        {
            row.Status = OutboxMessageStatus.Pending;
            row.NotBefore = retry.NotBefore;
            row.LockedUntil = null;
            row.LastError = retry.ErrorCode;
        }

        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(OutboxAttemptFailure failure, CancellationToken cancellationToken)
    {
        Held(failure.MessageId, failure.Attempt)?.Fail(failure.ErrorCode);
        return Task.CompletedTask;
    }

    private Row? Find(long messageId) => _rows.SingleOrDefault(row => row.Id == messageId);

    private Row? Held(long messageId, int attempt) =>
        Find(messageId) is { Status: OutboxMessageStatus.Processing } row && row.Attempt == attempt ? row : null;

    internal sealed class Row
    {
        public long Id { get; init; }

        public string Type { get; init; } = string.Empty;

        public string PayloadJson { get; init; } = string.Empty;

        public OutboxMessageStatus Status { get; set; }

        public int Attempt { get; set; }

        public DateTimeOffset? NotBefore { get; set; }

        public DateTimeOffset? LockedUntil { get; set; }

        public DateTimeOffset? SentAt { get; set; }

        public string? LastError { get; set; }

        public void Fail(string errorCode)
        {
            Status = OutboxMessageStatus.Failed;
            LockedUntil = null;
            LastError = errorCode;
        }
    }
}

/// <summary>What the test handlers saw, shared across their scoped instances.</summary>
internal sealed class DeliveryLog
{
    public List<OutboxDelivery> Delivered { get; } = [];

    /// <summary>Message ids whose next attempt throws, with the exception to throw.</summary>
    public Dictionary<long, Exception> Failures { get; } = [];
}

/// <summary>Delivers <see cref="OutboxMessageTypes.NotificationEmail"/>; throws when told to.</summary>
internal sealed class RecordingHandler(DeliveryLog log) : IOutboxHandler
{
    public string MessageType => OutboxMessageTypes.NotificationEmail;

    public Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        if (log.Failures.TryGetValue(delivery.MessageId, out var failure))
        {
            throw failure;
        }

        log.Delivered.Add(delivery);
        return Task.CompletedTask;
    }
}

/// <summary>Delivers <see cref="OutboxMessageTypes.NotificationSms"/>.</summary>
internal sealed class OtherTypeHandler(DeliveryLog log) : IOutboxHandler
{
    public string MessageType => OutboxMessageTypes.NotificationSms;

    public Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        log.Delivered.Add(delivery);
        return Task.CompletedTask;
    }
}

/// <summary>A second handler for <see cref="OutboxMessageTypes.NotificationEmail"/> (a wiring bug).</summary>
internal sealed class DuplicateEmailHandler : IOutboxHandler
{
    public string MessageType => OutboxMessageTypes.NotificationEmail;

    public Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken) => Task.CompletedTask;
}

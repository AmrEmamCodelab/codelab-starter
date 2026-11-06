using Codelab.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Starter.Application.Messaging;
using Starter.Infrastructure.Messaging;

namespace Starter.Api.Tests.Messaging;

/// <summary>
/// The drain state machine without a database: routing, the disabled switch, lease and reclaim, retry with back-off,
/// giving up, idempotent re-drains and the batch bounds (clock moved by hand).
/// </summary>
public sealed class OutboxDrainRunnerTests
{
    private const string TimeoutCode = "UPSTREAM_TIMEOUT";
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 8, 0, 0, TimeSpan.Zero);

    private readonly ManualClock _clock = new(Start);
    private readonly InMemoryOutboxStore _store = new();
    private readonly DeliveryLog _log = new();
    private readonly OutboxOptions _options = new();

    [Fact]
    public async Task Each_due_message_goes_to_the_handler_of_its_type_and_is_marked_sent()
    {
        var email = _store.Add(OutboxMessageTypes.NotificationEmail, payload: """{"to":"a"}""");
        var sms = _store.Add(OutboxMessageTypes.NotificationSms);

        var result = await Drain();

        Assert.Equal(new OutboxDrainResult(Sent: 2, Retried: 0, Failed: 0), result);
        Assert.Equal([new OutboxDelivery(email.Id, OutboxMessageTypes.NotificationEmail, """{"to":"a"}""", Attempt: 1), new OutboxDelivery(sms.Id, OutboxMessageTypes.NotificationSms, "{}", Attempt: 1)], _log.Delivered);
        Assert.All(_store.Rows, row => Assert.Equal(OutboxMessageStatus.Sent, row.Status));
        Assert.All(_store.Rows, row => Assert.Equal(Start, row.SentAt));
        Assert.All(_store.Rows, row => Assert.Null(row.LockedUntil));
    }

    [Fact]
    public async Task A_disabled_outbox_claims_nothing_and_leaves_messages_pending()
    {
        _options.Enabled = false;
        var row = _store.Add(OutboxMessageTypes.NotificationEmail);

        var result = await Drain();

        Assert.Equal(OutboxDrainResult.None, result);
        Assert.Equal(0, _store.ClaimCalls);
        Assert.Equal(OutboxMessageStatus.Pending, row.Status);
        Assert.Empty(_log.Delivered);
    }

    [Fact]
    public async Task A_message_is_not_delivered_before_its_not_before_time()
    {
        var row = _store.Add(OutboxMessageTypes.NotificationEmail, notBefore: Start.AddMinutes(10));

        Assert.Equal(OutboxDrainResult.None, await Drain());
        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, (await Drain()).Sent);

        Assert.Equal(OutboxMessageStatus.Sent, row.Status);
    }

    [Fact]
    public async Task A_sent_message_is_never_delivered_again()
    {
        _store.Add(OutboxMessageTypes.NotificationEmail);

        await Drain();
        _clock.Advance(TimeSpan.FromDays(1));
        var second = await Drain();

        Assert.Equal(OutboxDrainResult.None, second);
        Assert.Single(_log.Delivered);
    }

    [Fact]
    public async Task A_failed_attempt_returns_the_message_to_pending_after_the_back_off_with_only_the_classified_code()
    {
        var row = _store.Add(OutboxMessageTypes.NotificationEmail);
        _log.Failures[row.Id] = new TimeoutException("relay smtp.example.test refused a@b.test");

        var result = await Drain();

        Assert.Equal(new OutboxDrainResult(Sent: 0, Retried: 1, Failed: 0), result);
        Assert.Equal(OutboxMessageStatus.Pending, row.Status);
        Assert.Equal(1, row.Attempt);
        Assert.Equal(Start + OutboxDrainSchedule.BackoffAfter(1), row.NotBefore);
        Assert.Equal(TimeoutCode, row.LastError);
        Assert.Null(row.LockedUntil);
    }

    [Fact]
    public async Task A_retry_waits_for_its_back_off_and_hands_the_same_message_id_with_the_next_attempt()
    {
        var row = _store.Add(OutboxMessageTypes.NotificationEmail);
        _log.Failures[row.Id] = new TimeoutException();
        await Drain();
        _log.Failures.Clear();

        Assert.Equal(OutboxDrainResult.None, await Drain());
        _clock.Advance(OutboxDrainSchedule.BackoffAfter(1));
        var result = await Drain();

        Assert.Equal(1, result.Sent);
        Assert.Equal(new OutboxDelivery(row.Id, OutboxMessageTypes.NotificationEmail, "{}", Attempt: 2), Assert.Single(_log.Delivered));
        Assert.Equal(OutboxMessageStatus.Sent, row.Status);
        Assert.Null(row.LastError);
    }

    [Fact]
    public async Task A_message_that_fails_every_attempt_is_failed_after_the_last_one_and_never_tried_again()
    {
        var row = _store.Add(OutboxMessageTypes.NotificationEmail);
        _log.Failures[row.Id] = new InvalidOperationException();
        var results = new List<OutboxDrainResult>();

        for (var attempt = 1; attempt <= OutboxLimits.MaxAttempts; attempt++)
        {
            results.Add(await Drain());
            _clock.Advance(OutboxDrainSchedule.BackoffAfter(attempt));
        }

        _clock.Advance(TimeSpan.FromDays(1));
        results.Add(await Drain());

        Assert.Equal(OutboxLimits.MaxAttempts - 1, results.Sum(result => result.Retried));
        Assert.Equal(1, results.Sum(result => result.Failed));
        Assert.Equal(OutboxDrainResult.None, results[^1]);
        Assert.Equal(OutboxMessageStatus.Failed, row.Status);
        Assert.Equal(OutboxLimits.MaxAttempts, row.Attempt);
        Assert.Equal(DefaultFailureCodeResolver.InternalErrorCode, row.LastError);
    }

    [Fact]
    public async Task A_message_without_a_handler_is_failed_at_once_with_the_no_handler_code()
    {
        var row = _store.Add("unknown-type");

        var result = await Drain();

        Assert.Equal(new OutboxDrainResult(Sent: 0, Retried: 0, Failed: 1), result);
        Assert.Equal(OutboxMessageStatus.Failed, row.Status);
        Assert.Equal(OutboxErrorCodes.NoHandler, row.LastError);
        Assert.Equal(1, row.Attempt);
    }

    [Fact]
    public async Task A_message_claimed_by_a_crashed_drain_is_reclaimed_only_after_its_lease_expires()
    {
        var row = _store.Add(OutboxMessageTypes.NotificationEmail);
        await _store.ClaimAsync(new OutboxClaim(Start, Start + OutboxDrainSchedule.Lease, BatchSize: 1), CancellationToken.None);

        _clock.Advance(OutboxDrainSchedule.Lease - TimeSpan.FromSeconds(1));
        Assert.Equal(OutboxDrainResult.None, await Drain());
        _clock.Advance(TimeSpan.FromSeconds(1));
        var result = await Drain();

        Assert.Equal(1, result.Sent);
        Assert.Equal(new OutboxDelivery(row.Id, OutboxMessageTypes.NotificationEmail, "{}", Attempt: 2), Assert.Single(_log.Delivered));
    }

    [Fact]
    public async Task A_final_attempt_abandoned_by_a_crashed_drain_is_failed_with_the_lease_expired_code()
    {
        var row = _store.Add(OutboxMessageTypes.NotificationEmail);
        row.Status = OutboxMessageStatus.Processing;
        row.Attempt = OutboxLimits.MaxAttempts;
        row.LockedUntil = Start;

        var result = await Drain();

        Assert.Equal(new OutboxDrainResult(Sent: 0, Retried: 0, Failed: 1), result);
        Assert.Equal(OutboxMessageStatus.Failed, row.Status);
        Assert.Equal(OutboxErrorCodes.LeaseExpired, row.LastError);
        Assert.Empty(_log.Delivered);
    }

    [Fact]
    public async Task One_pass_claims_batch_after_batch_until_a_short_batch()
    {
        _options.BatchSize = 2;
        for (var index = 0; index < 5; index++)
        {
            _store.Add(OutboxMessageTypes.NotificationEmail);
        }

        var result = await Drain();

        Assert.Equal(5, result.Sent);
        Assert.Equal(3, _store.ClaimCalls);
    }

    [Fact]
    public async Task One_pass_stops_after_the_maximum_number_of_batches()
    {
        _options.BatchSize = 1;
        var total = OutboxDrainSchedule.MaxBatchesPerPass + 3;
        for (var index = 0; index < total; index++)
        {
            _store.Add(OutboxMessageTypes.NotificationEmail);
        }

        var result = await Drain();

        Assert.Equal(OutboxDrainSchedule.MaxBatchesPerPass, result.Sent);
        Assert.Equal(3, _store.Rows.Count(row => row.Status == OutboxMessageStatus.Pending));
    }

    [Fact]
    public async Task A_stopping_drain_propagates_the_cancellation_and_leaves_the_lease_to_return_the_message()
    {
        var row = _store.Add(OutboxMessageTypes.NotificationEmail);
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();
        _log.Failures[row.Id] = new OperationCanceledException(stopping.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner().DrainOnceAsync(stopping.Token));

        Assert.Equal(OutboxMessageStatus.Processing, row.Status);
        Assert.Equal(Start + OutboxDrainSchedule.Lease, row.LockedUntil);
    }

    [Fact]
    public async Task A_handler_timing_out_on_its_own_is_a_failed_attempt_not_a_stopping_drain()
    {
        var row = _store.Add(OutboxMessageTypes.NotificationEmail);
        _log.Failures[row.Id] = new TaskCanceledException("handler timeout");

        var result = await Drain();

        Assert.Equal(1, result.Retried);
        Assert.Equal(OutboxMessageStatus.Pending, row.Status);
    }

    [Fact]
    public async Task Two_handlers_for_one_type_fail_the_pass_loudly()
    {
        _store.Add(OutboxMessageTypes.NotificationEmail);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(services => services.AddOutboxHandler<DuplicateEmailHandler>()).DrainOnceAsync(CancellationToken.None));

        Assert.Contains(OutboxMessageTypes.NotificationEmail, refused.Message, StringComparison.Ordinal);
        Assert.Empty(_log.Delivered);
    }

    private Task<OutboxDrainResult> Drain() => Runner().DrainOnceAsync(CancellationToken.None);

    private OutboxDrainRunner Runner(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_log);
        services.AddOutboxHandler<RecordingHandler>().AddOutboxHandler<OtherTypeHandler>();
        extra?.Invoke(services);
        var provider = services.BuildServiceProvider();
        var dispatcher = new OutboxDispatcher(provider.GetRequiredService<IServiceScopeFactory>(), new TimeoutAwareCodes(), NullLogger<OutboxDispatcher>.Instance);
        return new OutboxDrainRunner(_store, dispatcher, new FixedOptionsMonitor(_options), _clock, NullLogger<OutboxDrainRunner>.Instance);
    }

    /// <summary>Classifies a timeout as <see cref="TimeoutCode"/> and everything else like the default resolver.</summary>
    private sealed class TimeoutAwareCodes : IFailureCodeResolver
    {
        public FailureCode Resolve(Exception? exception) => exception is TimeoutException
            ? new FailureCode(TimeoutCode, LogLevel.Warning)
            : new DefaultFailureCodeResolver().Resolve(exception);
    }

    private sealed class FixedOptionsMonitor(OutboxOptions value) : IOptionsMonitor<OutboxOptions>
    {
        public OutboxOptions CurrentValue => value;

        public OutboxOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<OutboxOptions, string?> listener) => null;
    }
}

/// <summary>The drain timing and the stored error.</summary>
public sealed class OutboxDrainScheduleTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 15)]
    [InlineData(4, 60)]
    [InlineData(7, 60)]
    public void The_back_off_grows_from_one_minute_to_one_hour(int attempt, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), OutboxDrainSchedule.BackoffAfter(attempt));

    [Fact]
    public void Every_attempt_together_spans_hours_and_the_lease_is_shorter_than_the_drain_interval()
    {
        var span = Enumerable.Range(1, OutboxLimits.MaxAttempts - 1).Sum(attempt => OutboxDrainSchedule.BackoffAfter(attempt).TotalMinutes);

        Assert.True(span >= 4 * 60, $"{span} minutes");
        Assert.Equal(TimeSpan.FromMinutes(5), OutboxDrainSchedule.Lease);
    }

    [Fact]
    public void A_stored_error_is_cut_to_the_column_and_a_short_one_kept()
    {
        var longCode = new string('E', OutboxColumnLimits.LastErrorMaxLength + 10);

        Assert.Equal(OutboxColumnLimits.LastErrorMaxLength, SqlOutboxStore.ToStoredError(longCode).Length);
        Assert.Equal(OutboxErrorCodes.NoHandler, SqlOutboxStore.ToStoredError(OutboxErrorCodes.NoHandler));
        Assert.Throws<ArgumentException>(() => SqlOutboxStore.ToStoredError(" "));
    }

    [Fact]
    public void The_options_validator_accepts_the_bounds_and_refuses_outside_them()
    {
        var validator = new OutboxOptionsValidator();

        Assert.True(validator.Validate(name: null, new OutboxOptions { BatchSize = 1 }).Succeeded);
        Assert.True(validator.Validate(name: null, new OutboxOptions { BatchSize = OutboxOptions.MaxBatchSize }).Succeeded);
        Assert.True(validator.Validate(name: null, new OutboxOptions { BatchSize = OutboxOptions.MaxBatchSize + 1 }).Failed);
        Assert.True(validator.Validate(name: null, new OutboxOptions { BatchSize = 0 }).Failed);
    }
}

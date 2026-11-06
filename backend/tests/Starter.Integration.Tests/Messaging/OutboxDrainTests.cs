using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Starter.Application.Messaging;
using Starter.Domain.Localization;
using Starter.Infrastructure.Messaging;
using Starter.Integration.Tests.Support;

namespace Starter.Integration.Tests.Messaging;

/// <summary>
/// Exit criterion: an outbox message is drained once and is idempotent on retry, on real SQL. A probe message is
/// delivered once and never again; a real notification e-mail whose drain "crashed" after the handler sent it (no Sent
/// mark, the lease left to expire) is redelivered with the same message id and NOT e-mailed twice; a failing handler
/// is retried with the back-off and ends Failed with a code only.
/// </summary>
[Collection(Phase5HostGroup.Name)]
public sealed class OutboxDrainTests(Phase5Host host)
{
    private const string TestNotification = "/api/admin/notifications/test";
    private const int ClaimBatchSize = 50;
    private const int RedeliveredAttempt = 2;
    private const char StateSeparator = '|';
    private const int StatusPart = 0;
    private const int AttemptPart = 1;
    private const int ErrorPart = 2;
    private static readonly TimeSpan JustOver = TimeSpan.FromSeconds(1);

    private const string MessageState = "SELECT CONCAT([Status], '|', [Attempt], '|', [LastError]) FROM [messaging].[OutboxMessages] WHERE [Id] = @id";
    private const string MessageOfProbe = "SELECT [Id] FROM [messaging].[OutboxMessages] WHERE [PayloadJson] = @payload";
    private const string LastMessageId = "SELECT ISNULL(MAX([Id]), 0) FROM [messaging].[OutboxMessages]";
    private const string EmailMessagesAfter = "SELECT [Id] FROM [messaging].[OutboxMessages] WHERE [Type] = @type AND [Id] > @after";

    [Fact]
    public async Task A_message_is_delivered_once_and_a_second_drain_does_not_deliver_it_again()
    {
        var probe = await EnqueueProbeAsync(ProbeMessageTypes.Idempotent);
        var id = await MessageIdOfAsync(probe);

        await host.DrainOutboxAsync();
        await host.DrainOutboxAsync();

        var delivery = Assert.Single(host.Probes.DeliveriesOf(probe.Probe));
        Assert.Equal(id, delivery.MessageId);
        Assert.Equal(1, delivery.Attempt);
        Assert.Equal(1, host.Probes.EffectsOf(probe.Probe));
        Assert.Equal(new MessageRow(OutboxMessageStatus.Sent, Attempt: 1, LastError: string.Empty), await StateAsync(id));
    }

    [Fact]
    public async Task A_drain_that_crashed_after_the_handler_redelivers_after_the_lease_without_sending_the_email_twice()
    {
        var mailbox = $"crash.{TestNames.Suffix()}@{PiiCanaries.EmailDomain}";
        using var recipient = host.Recipients.Use(new RecipientOverride(mailbox, Mobile: null, AppLanguage.English));
        var after = await LastIdAsync();
        using (var api = host.CreateApi())
        using (var accepted = await api.PostAsync(TestNotification, body: null, await host.AdminTokenAsync()))
        {
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }

        var emailId = Assert.Single(await EmailMessagesAfterAsync(after));
        await DeliverWithoutMarkingSentAsync(); // the drain process dies after the handler returned
        Assert.Single(host.Mail.To(mailbox));
        Assert.Equal(OutboxMessageStatus.Processing, (await StateAsync(emailId)).Status);

        try
        {
            host.Clock.Advance(OutboxDrainSchedule.Lease + JustOver);
            await host.DrainOutboxAsync(); // reclaims the expired lease: same message id, attempt 2
            await host.DrainOutboxAsync();
        }
        finally
        {
            host.Clock.Reset();
        }

        Assert.Single(host.Mail.To(mailbox)); // the handler saw its channel already Sent
        Assert.Equal(new MessageRow(OutboxMessageStatus.Sent, Attempt: RedeliveredAttempt, LastError: string.Empty), await StateAsync(emailId));
    }

    [Fact]
    public async Task A_failing_handler_is_retried_with_the_back_off_and_ends_Failed_with_a_code_only()
    {
        var probe = await EnqueueProbeAsync(ProbeMessageTypes.Failing);
        var id = await MessageIdOfAsync(probe);
        try
        {
            for (var attempt = 1; attempt < OutboxLimits.MaxAttempts; attempt++)
            {
                await host.DrainOutboxAsync();
                var state = await StateAsync(id);
                Assert.Equal(OutboxMessageStatus.Pending, state.Status);
                Assert.Equal(attempt, state.Attempt);
                await host.DrainOutboxAsync(); // not due yet: the back-off holds it
                Assert.Equal(attempt, (await StateAsync(id)).Attempt);
                host.Clock.Advance(OutboxDrainSchedule.BackoffAfter(attempt) + JustOver);
            }

            await host.DrainOutboxAsync();
        }
        finally
        {
            host.Clock.Reset();
        }

        var final = await StateAsync(id);
        Assert.Equal(OutboxMessageStatus.Failed, final.Status);
        Assert.Equal(OutboxLimits.MaxAttempts, final.Attempt);
        Assert.False(string.IsNullOrEmpty(final.LastError));
        Assert.DoesNotContain(FailingProbeHandler.FailureText, final.LastError, StringComparison.Ordinal);
        Assert.Equal(OutboxLimits.MaxAttempts, host.Probes.DeliveriesOf(probe.Probe).Count);
        Assert.All(host.Probes.DeliveriesOf(probe.Probe), delivery => Assert.Equal(id, delivery.MessageId));
        await host.DrainOutboxAsync();
        Assert.Equal(OutboxLimits.MaxAttempts, host.Probes.DeliveriesOf(probe.Probe).Count); // Failed is never retried automatically
    }

    private async Task<ProbePayload> EnqueueProbeAsync(string type)
    {
        var probe = new ProbePayload(Guid.NewGuid().ToString("N"));
        await using var scope = host.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IOutbox>()
            .EnqueueAsync(new OutboxMessage(type, probe.ToJson(), NotBefore: null), CancellationToken.None);
        return probe;
    }

    /// <summary>Claims every due message and runs its handler, then stops: the Sent mark is never written.</summary>
    private async Task DeliverWithoutMarkingSentAsync()
    {
        await using var scope = host.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var now = host.Clock.GetUtcNow();
        var claimed = await services.GetRequiredService<IOutboxStore>()
            .ClaimAsync(new OutboxClaim(now, LockedUntil: now + OutboxDrainSchedule.Lease, BatchSize: ClaimBatchSize), CancellationToken.None);
        var handlers = services.GetServices<IOutboxHandler>().ToDictionary(handler => handler.MessageType, StringComparer.Ordinal);
        foreach (var delivery in claimed.Where(delivery => delivery.Type == OutboxMessageTypes.NotificationEmail))
        {
            await handlers[delivery.Type].HandleAsync(delivery, CancellationToken.None);
        }
    }

    private async Task<long> MessageIdOfAsync(ProbePayload probe) =>
        Convert.ToInt64(
            await TestSqlServer.ScalarAsync(host.DatabaseName, MessageOfProbe, new Dictionary<string, object> { ["@payload"] = probe.ToJson() }),
            CultureInfo.InvariantCulture);

    private async Task<long> LastIdAsync() =>
        Convert.ToInt64(await TestSqlServer.ScalarAsync(host.DatabaseName, LastMessageId), CultureInfo.InvariantCulture);

    private async Task<IReadOnlyList<long>> EmailMessagesAfterAsync(long after) =>
        (await TestSqlServer.ReadStringsAsync(
            host.DatabaseName,
            EmailMessagesAfter,
            new Dictionary<string, object> { ["@type"] = OutboxMessageTypes.NotificationEmail, ["@after"] = after }))
        .Select(value => long.Parse(value, CultureInfo.InvariantCulture))
        .ToList();

    private async Task<MessageRow> StateAsync(long id)
    {
        var text = (string)(await TestSqlServer.ScalarAsync(host.DatabaseName, MessageState, new Dictionary<string, object> { ["@id"] = id }))!;
        var parts = text.Split(StateSeparator);
        return new MessageRow(
            (OutboxMessageStatus)int.Parse(parts[StatusPart], CultureInfo.InvariantCulture),
            Attempt: int.Parse(parts[AttemptPart], CultureInfo.InvariantCulture),
            LastError: parts[ErrorPart]);
    }

    private sealed record MessageRow(OutboxMessageStatus Status, int Attempt, string LastError);
}

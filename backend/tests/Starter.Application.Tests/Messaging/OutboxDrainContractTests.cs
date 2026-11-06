using Starter.Application.Messaging;

namespace Starter.Application.Tests.Messaging;

/// <summary>The phase 5 outbox ports: the wire names the drain routes on, the limits and the drain result.</summary>
public sealed class OutboxDrainContractTests
{
    [Theory]
    [InlineData(OutboxMessageTypes.AccountActivation, "account-activation")]
    [InlineData(OutboxMessageTypes.NotificationEmail, "notification-email")]
    [InlineData(OutboxMessageTypes.NotificationSms, "notification-sms")]
    public void Every_message_type_keeps_the_name_stored_rows_carry(string constant, string stored) =>
        Assert.Equal(stored, constant);

    [Fact]
    public void Every_message_type_fits_the_type_column()
    {
        string[] types = [OutboxMessageTypes.AccountActivation, OutboxMessageTypes.NotificationEmail, OutboxMessageTypes.NotificationSms];

        Assert.All(types, type => Assert.InRange(type.Length, 1, OutboxLimits.TypeMaxLength));
        Assert.Equal(types.Length, types.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_message_gets_eight_attempts() => Assert.Equal(8, OutboxLimits.MaxAttempts);

    [Fact]
    public void The_empty_drain_result_counts_nothing() =>
        Assert.Equal(new OutboxDrainResult(Sent: 0, Retried: 0, Failed: 0), OutboxDrainResult.None);

    [Fact]
    public void A_delivery_is_a_value_keyed_by_its_message_id_and_attempt()
    {
        var first = new OutboxDelivery(MessageId: 41, OutboxMessageTypes.NotificationEmail, "{}", Attempt: 1);

        Assert.Equal(first, first with { });
        Assert.NotEqual(first, first with { Attempt = 2 });
        Assert.Equal(41, (first with { Attempt = 2 }).MessageId);
    }
}

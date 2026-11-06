using Starter.Application.Messaging;
using Starter.Application.Notifications;
using Starter.Domain.Exceptions;
using Starter.Domain.Localization;
using Starter.Modules.Notifications.Application;
using Starter.Modules.Notifications.Application.Delivery;
using Starter.Modules.Notifications.Domain;
using Starter.Modules.Notifications.Tests.Fakes;

namespace Starter.Modules.Notifications.Tests.Application;

public sealed class NotificationWriterTests
{
    private const NotificationChannels Every = NotificationChannels.InApp | NotificationChannels.Email | NotificationChannels.Sms;

    [Fact]
    public async Task Every_channel_writes_the_bell_item_now_and_queues_email_and_sms()
    {
        var harness = new NotificationsHarness();

        var id = (await harness.Writer().WriteAsync(NotificationsHarness.Test(Every), CancellationToken.None)).Value;

        var row = Assert.Single(harness.Store.Saved);
        Assert.Equal(id, row.Id);
        Assert.Equal("إشعار تجريبي", row.TitleAr);
        Assert.Equal("Test notification", row.TitleEn);
        Assert.Contains("2026-11-06 09:30 UTC", row.BodyEn, StringComparison.Ordinal);
        Assert.Equal(ChannelDeliveryState.Pending, row.EmailState);
        Assert.Equal(ChannelDeliveryState.Pending, row.SmsState);
        Assert.NotNull(row.PlaceholdersJson);
        Assert.Equal(harness.Clock.GetUtcNow(), row.CreatedAt);
        Assert.Equal([OutboxMessageTypes.NotificationEmail, OutboxMessageTypes.NotificationSms], harness.Outbox.Messages.Select(message => message.Type));
        Assert.Equal(new DeliveryPayload(id, NotificationChannels.Email), DeliveryPayload.TryRead(harness.Outbox.Messages[0].PayloadJson));
    }

    [Fact]
    public async Task The_payload_names_the_notification_and_channel_only()
    {
        var harness = new NotificationsHarness();

        await harness.Writer().WriteAsync(NotificationsHarness.Test(NotificationChannels.Email, sentAt: "secret-value"), CancellationToken.None);

        Assert.DoesNotContain("secret-value", Assert.Single(harness.Outbox.Messages).PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sms_is_skipped_while_disabled_without_needing_its_text()
    {
        var harness = new NotificationsHarness(smsEnabled: false);
        harness.Templates.Texts.RemoveAll(text => text.Channel == NotificationChannels.Sms);

        await harness.Writer().WriteAsync(NotificationsHarness.Test(Every), CancellationToken.None);

        Assert.Equal(ChannelDeliveryState.Disabled, Assert.Single(harness.Store.Saved).SmsState);
        Assert.Equal(OutboxMessageTypes.NotificationEmail, Assert.Single(harness.Outbox.Messages).Type);
    }

    [Fact]
    public async Task An_in_app_only_notification_queues_nothing_and_keeps_no_placeholders()
    {
        var harness = new NotificationsHarness();

        await harness.Writer().WriteAsync(NotificationsHarness.Test(NotificationChannels.InApp), CancellationToken.None);

        var row = Assert.Single(harness.Store.Saved);
        Assert.Null(row.PlaceholdersJson);
        Assert.Equal(ChannelDeliveryState.NotRequested, row.EmailState);
        Assert.Empty(harness.Outbox.Messages);
    }

    [Fact]
    public async Task An_email_only_notification_has_no_bell_texts()
    {
        var harness = new NotificationsHarness();

        await harness.Writer().WriteAsync(NotificationsHarness.Test(NotificationChannels.Email), CancellationToken.None);

        var row = Assert.Single(harness.Store.Saved);
        Assert.False(row.IsInApp);
        Assert.Null(row.TitleAr);
    }

    [Fact]
    public async Task A_missing_placeholder_writes_and_queues_nothing()
    {
        var harness = new NotificationsHarness();
        var notification = new UserNotification(
            NotificationsHarness.UserId, NotificationTemplateKeys.TestNotification, new Dictionary<string, string>(), Every, Link: null);

        var outcome = await harness.Writer().WriteAsync(notification, CancellationToken.None);

        Assert.Equal(NotificationsErrorCodes.PlaceholderMissing, outcome.Refusal!.Code);
        Assert.Empty(harness.Store.Saved);
        Assert.Empty(harness.Outbox.Messages);
    }

    [Theory]
    [InlineData(NotificationChannels.None)]
    [InlineData((NotificationChannels)8)]
    public async Task No_or_an_unknown_channel_is_refused(NotificationChannels channels)
    {
        var outcome = await new NotificationsHarness().Writer().WriteAsync(NotificationsHarness.Test(channels), CancellationToken.None);

        Assert.Equal(NotificationsErrorCodes.InvalidNotification, outcome.Refusal!.Code);
    }

    [Fact]
    public async Task A_too_long_link_or_a_blank_key_is_refused()
    {
        var writer = new NotificationsHarness().Writer();
        var longLink = NotificationsHarness.Test(NotificationChannels.InApp) with { Link = new string('a', NotificationsLimits.LinkMaxLength + 1) };
        var blankKey = NotificationsHarness.Test(NotificationChannels.InApp) with { TemplateKey = " " };

        Assert.Equal(NotificationsErrorCodes.InvalidNotification, (await writer.WriteAsync(longLink, CancellationToken.None)).Refusal!.Code);
        Assert.Equal(NotificationsErrorCodes.InvalidNotification, (await writer.WriteAsync(blankKey, CancellationToken.None)).Refusal!.Code);
    }

    [Fact]
    public async Task An_addressed_email_is_stored_with_its_address_and_language_and_queued()
    {
        var harness = new NotificationsHarness();
        var notification = new AddressedNotification(
            " inbox@example.test ", NotificationTemplateKeys.TestNotification, NotificationsHarness.SentAt("now"), AppLanguage.Arabic);

        await harness.Writer().WriteAsync(notification, CancellationToken.None);

        var row = Assert.Single(harness.Store.Saved);
        Assert.Null(row.UserId);
        Assert.Equal("inbox@example.test", row.EmailAddress);
        Assert.Equal(AppLanguage.Arabic, row.Language);
        Assert.False(row.IsInApp);
        Assert.Equal(OutboxMessageTypes.NotificationEmail, Assert.Single(harness.Outbox.Messages).Type);
    }

    [Fact]
    public async Task The_service_throws_a_business_rule_exception_with_the_refusal_code()
    {
        var service = new NotificationsHarness().Service();
        var notification = new AddressedNotification(
            "inbox@example.test", NotificationTemplateKeys.ContactReceived, new Dictionary<string, string>(), AppLanguage.English);

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() => service.SendToAddressAsync(notification, CancellationToken.None));

        Assert.Equal(NotificationsErrorCodes.PlaceholderMissing, exception.Code);
    }
}

public sealed class NotificationDeliveryTests
{
    private const NotificationChannels EmailAndSms = NotificationChannels.Email | NotificationChannels.Sms;

    [Fact]
    public async Task The_email_handler_sends_in_the_recipients_language_and_records_sent()
    {
        var harness = new NotificationsHarness();
        await harness.Writer().WriteAsync(NotificationsHarness.Test(EmailAndSms), CancellationToken.None);
        var email = harness.Outbox.Messages.Single(message => message.Type == OutboxMessageTypes.NotificationEmail);

        await harness.EmailHandler().HandleAsync(NotificationsHarness.DeliveryOf(email), CancellationToken.None);

        var sent = Assert.Single(harness.Email.Sent);
        Assert.Equal(NotificationsHarness.UserEmail, sent.To);
        Assert.Equal("Test notification", sent.Subject);
        var row = Assert.Single(harness.Store.Saved);
        Assert.Equal(ChannelDeliveryState.Sent, row.EmailState);
        Assert.Equal(harness.Clock.GetUtcNow(), row.EmailSentAt);
        Assert.NotNull(row.PlaceholdersJson); // the SMS still waits
    }

    [Fact]
    public async Task The_sms_handler_sends_Arabic_text_to_an_Arabic_reader_and_drops_the_placeholders_when_done()
    {
        var harness = new NotificationsHarness();
        harness.Recipients.Users[NotificationsHarness.UserId] = harness.Recipients.Users[NotificationsHarness.UserId] with { Language = AppLanguage.Arabic };
        await harness.Writer().WriteAsync(NotificationsHarness.Test(EmailAndSms), CancellationToken.None);

        foreach (var message in harness.Outbox.Messages)
        {
            var handler = message.Type == OutboxMessageTypes.NotificationSms ? (IOutboxHandler)harness.SmsHandler() : harness.EmailHandler();
            await handler.HandleAsync(NotificationsHarness.DeliveryOf(message), CancellationToken.None);
        }

        var sms = Assert.Single(harness.Sms.Sent);
        Assert.Equal(NotificationsHarness.UserMobile, sms.To);
        Assert.Equal(AppLanguage.Arabic, sms.Language);
        Assert.StartsWith("رسالة نصية تجريبية", sms.Text, StringComparison.Ordinal);
        Assert.Null(Assert.Single(harness.Store.Saved).PlaceholdersJson);
    }

    [Fact]
    public async Task A_redelivered_message_is_not_sent_twice()
    {
        var harness = new NotificationsHarness();
        await harness.Writer().WriteAsync(NotificationsHarness.Test(NotificationChannels.Email), CancellationToken.None);
        var delivery = NotificationsHarness.DeliveryOf(Assert.Single(harness.Outbox.Messages));
        var handler = harness.EmailHandler();

        await handler.HandleAsync(delivery, CancellationToken.None);
        await handler.HandleAsync(delivery with { Attempt = 2 }, CancellationToken.None);

        Assert.Single(harness.Email.Sent);
    }

    [Fact]
    public async Task A_sender_failure_propagates_so_the_outbox_retries_and_the_channel_stays_pending()
    {
        var harness = new NotificationsHarness();
        await harness.Writer().WriteAsync(NotificationsHarness.Test(NotificationChannels.Email), CancellationToken.None);
        var delivery = NotificationsHarness.DeliveryOf(Assert.Single(harness.Outbox.Messages));
        harness.Email.FailWith = new IOException("relay down");

        await Assert.ThrowsAsync<IOException>(() => harness.EmailHandler().HandleAsync(delivery, CancellationToken.None));
        Assert.Equal(ChannelDeliveryState.Pending, Assert.Single(harness.Store.Saved).EmailState);

        harness.Email.FailWith = null;
        await harness.EmailHandler().HandleAsync(delivery with { Attempt = 2 }, CancellationToken.None);
        Assert.Single(harness.Email.Sent);
    }

    [Fact]
    public async Task A_recipient_without_an_address_ends_no_address_without_sending()
    {
        var harness = new NotificationsHarness();
        harness.Recipients.Users[NotificationsHarness.UserId] = harness.Recipients.Users[NotificationsHarness.UserId] with { Mobile = null };
        await harness.Writer().WriteAsync(NotificationsHarness.Test(NotificationChannels.Sms), CancellationToken.None);

        await harness.SmsHandler().HandleAsync(NotificationsHarness.DeliveryOf(Assert.Single(harness.Outbox.Messages)), CancellationToken.None);

        Assert.Empty(harness.Sms.Sent);
        Assert.Equal(ChannelDeliveryState.NoAddress, Assert.Single(harness.Store.Saved).SmsState);
    }

    [Fact]
    public async Task An_unknown_recipient_ends_no_address()
    {
        var harness = new NotificationsHarness();
        await harness.Writer().WriteAsync(NotificationsHarness.Test(NotificationChannels.Email), CancellationToken.None);
        harness.Recipients.Users.Clear();

        await harness.EmailHandler().HandleAsync(NotificationsHarness.DeliveryOf(Assert.Single(harness.Outbox.Messages)), CancellationToken.None);

        Assert.Equal(ChannelDeliveryState.NoAddress, Assert.Single(harness.Store.Saved).EmailState);
    }

    [Fact]
    public async Task An_addressed_email_goes_to_its_stored_address_in_its_language()
    {
        var harness = new NotificationsHarness();
        var notification = new AddressedNotification(
            "inbox@example.test", NotificationTemplateKeys.TestNotification, NotificationsHarness.SentAt("now"), AppLanguage.Arabic);
        await harness.Writer().WriteAsync(notification, CancellationToken.None);

        await harness.EmailHandler().HandleAsync(NotificationsHarness.DeliveryOf(Assert.Single(harness.Outbox.Messages)), CancellationToken.None);

        var sent = Assert.Single(harness.Email.Sent);
        Assert.Equal("inbox@example.test", sent.To);
        Assert.Equal("إشعار تجريبي", sent.Subject);
        Assert.Contains("dir=\"rtl\"", sent.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_payload_of_another_channel_an_unreadable_one_or_a_missing_notification_sends_nothing()
    {
        var harness = new NotificationsHarness();
        var handler = harness.EmailHandler();
        var smsPayload = new DeliveryPayload(1, NotificationChannels.Sms).ToJson();
        var missing = new DeliveryPayload(99, NotificationChannels.Email).ToJson();

        await handler.HandleAsync(new OutboxDelivery(1, OutboxMessageTypes.NotificationEmail, smsPayload, Attempt: 1), CancellationToken.None);
        await handler.HandleAsync(new OutboxDelivery(2, OutboxMessageTypes.NotificationEmail, "not json", Attempt: 1), CancellationToken.None);
        await handler.HandleAsync(new OutboxDelivery(3, OutboxMessageTypes.NotificationEmail, missing, Attempt: 1), CancellationToken.None);

        Assert.Empty(harness.Email.Sent);
    }

    [Fact]
    public void Each_handler_declares_its_outbox_type()
    {
        var harness = new NotificationsHarness();

        Assert.Equal(OutboxMessageTypes.NotificationEmail, harness.EmailHandler().MessageType);
        Assert.Equal(OutboxMessageTypes.NotificationSms, harness.SmsHandler().MessageType);
        Assert.Throws<ArgumentOutOfRangeException>(() => DeliveryPayload.MessageTypeOf(NotificationChannels.InApp));
    }
}

public sealed class ImmediateEmailTests
{
    private static UserNotification Activation(long userId) => new(
        userId,
        NotificationTemplateKeys.AccountActivation,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NotificationPlaceholders.ActivationLink] = "http://localhost:4280/activate#ticket=abc&x=1",
            [NotificationPlaceholders.ExpiresAt] = "2026-11-07 09:30",
        },
        NotificationChannels.InApp,
        Link: null);

    [Fact]
    public async Task It_sends_now_in_the_recipients_language_and_stores_nothing()
    {
        var harness = new NotificationsHarness();

        await harness.Service().SendImmediateEmailAsync(Activation(NotificationsHarness.UserId), CancellationToken.None);

        var sent = Assert.Single(harness.Email.Sent);
        Assert.Equal("Activate your account", sent.Subject);
        Assert.Contains("href=\"http://localhost:4280/activate#ticket=abc&amp;x=1\"", sent.HtmlBody, StringComparison.Ordinal);
        Assert.Empty(harness.Store.Saved);
        Assert.Empty(harness.Outbox.Messages);
    }

    [Fact]
    public async Task An_unknown_user_or_a_user_without_email_is_refused()
    {
        var harness = new NotificationsHarness();
        harness.Recipients.Users[7] = new NotificationRecipient(7, Email: null, Mobile: null, AppLanguage.Arabic);
        var immediate = new ImmediateEmails(harness.Composer, harness.Recipients, new EmailChannel(harness.Email));

        Assert.Equal(NotificationsErrorCodes.RecipientNotFound, (await immediate.SendAsync(Activation(99), CancellationToken.None)).Refusal!.Code);
        Assert.Equal(NotificationsErrorCodes.RecipientHasNoEmail, (await immediate.SendAsync(Activation(7), CancellationToken.None)).Refusal!.Code);
        var thrown = await Assert.ThrowsAsync<BusinessRuleException>(() => harness.Service().SendImmediateEmailAsync(Activation(7), CancellationToken.None));
        Assert.Equal(NotificationsErrorCodes.RecipientHasNoEmail, thrown.Code);
        Assert.Empty(harness.Email.Sent);
    }
}

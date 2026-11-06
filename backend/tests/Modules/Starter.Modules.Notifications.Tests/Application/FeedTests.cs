using Starter.Application.Messaging;
using Starter.Application.Notifications;
using Starter.Domain.Localization;
using Starter.Modules.Notifications.Application;
using Starter.Modules.Notifications.Application.Feed;
using Starter.Modules.Notifications.Domain;
using Starter.Modules.Notifications.Tests.Fakes;

namespace Starter.Modules.Notifications.Tests.Application;

public sealed class FeedTests
{
    [Fact]
    public async Task Pages_are_newest_first_with_a_cursor_and_has_more()
    {
        var harness = await HarnessWith(count: 5);
        var feed = new GetNotificationFeed(harness.Owners(), harness.Store, harness.Language, harness.RuntimeSettings);

        var first = (await feed.ExecuteAsync(new FeedRequest(BeforeId: null, Take: 2), CancellationToken.None)).Value;
        var second = (await feed.ExecuteAsync(new FeedRequest(first.Items[^1].Id, Take: 2), CancellationToken.None)).Value;
        var last = (await feed.ExecuteAsync(new FeedRequest(second.Items[^1].Id, Take: 2), CancellationToken.None)).Value;

        Assert.Equal([5L, 4L], first.Items.Select(item => item.Id));
        Assert.True(first.HasMore);
        Assert.Equal([3L, 2L], second.Items.Select(item => item.Id));
        Assert.Equal([1L], last.Items.Select(item => item.Id));
        Assert.False(last.HasMore);
    }

    [Fact]
    public async Task Texts_follow_the_request_language()
    {
        var harness = await HarnessWith(count: 1);
        var feed = new GetNotificationFeed(harness.Owners(), harness.Store, harness.Language, harness.RuntimeSettings);

        var arabic = Assert.Single((await feed.ExecuteAsync(new FeedRequest(null, null), CancellationToken.None)).Value.Items);
        harness.Language.Language = AppLanguage.English;
        var english = Assert.Single((await feed.ExecuteAsync(new FeedRequest(null, null), CancellationToken.None)).Value.Items);

        Assert.Equal("إشعار تجريبي", arabic.TitleText);
        Assert.Equal("Test notification", english.TitleText);
        Assert.False(english.Read);
    }

    private const int AdminDefaultPageSize = 12;

    [Theory]
    [InlineData(null, AdminDefaultPageSize)]
    [InlineData(0, AdminDefaultPageSize)]
    [InlineData(-3, AdminDefaultPageSize)]
    [InlineData(5, 5)]
    [InlineData(1000, NotificationsLimits.MaxFeedPageSize)]
    public void The_page_size_is_defaulted_and_capped(int? take, int expected) =>
        Assert.Equal(expected, new FeedRequest(BeforeId: null, take).PageSize(defaultPageSize: AdminDefaultPageSize));

    [Fact]
    public void A_default_above_the_cap_is_capped_too() =>
        Assert.Equal(NotificationsLimits.MaxFeedPageSize, new FeedRequest(BeforeId: null, Take: null).PageSize(defaultPageSize: NotificationsLimits.MaxFeedPageSize + 1));

    [Fact]
    public async Task A_request_without_a_size_gets_the_administrators_page_size_setting()
    {
        var harness = await HarnessWith(count: 5);
        harness.RuntimeSettings.FeedPageSize = 2;
        var feed = new GetNotificationFeed(harness.Owners(), harness.Store, harness.Language, harness.RuntimeSettings);

        var page = (await feed.ExecuteAsync(new FeedRequest(BeforeId: null, Take: null), CancellationToken.None)).Value;
        harness.RuntimeSettings.FeedPageSize = 4;
        var bigger = (await feed.ExecuteAsync(new FeedRequest(BeforeId: null, Take: null), CancellationToken.None)).Value;

        Assert.Equal([5L, 4L], page.Items.Select(item => item.Id));
        Assert.True(page.HasMore);
        Assert.Equal(4, bigger.Items.Count);
    }

    [Fact]
    public async Task Email_only_notifications_and_other_users_items_stay_out_of_the_feed()
    {
        var harness = await HarnessWith(count: 1);
        await harness.Writer().WriteAsync(NotificationsHarness.Test(NotificationChannels.Email), CancellationToken.None);
        await harness.Writer().WriteAsync(NotificationsHarness.Test(NotificationChannels.InApp) with { UserId = 7 }, CancellationToken.None);
        var feed = new GetNotificationFeed(harness.Owners(), harness.Store, harness.Language, harness.RuntimeSettings);

        var page = (await feed.ExecuteAsync(new FeedRequest(null, null), CancellationToken.None)).Value;

        Assert.Equal([1L], page.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Marking_read_is_idempotent_and_lowers_the_unread_count()
    {
        var harness = await HarnessWith(count: 2);
        var markRead = new MarkNotificationRead(harness.Owners(), harness.Store, harness.Clock);
        var count = new GetUnreadNotificationCount(harness.Owners(), harness.Store);

        Assert.Equal(2, (await count.ExecuteAsync(CancellationToken.None)).Value);
        Assert.True((await markRead.ExecuteAsync(1, CancellationToken.None)).IsSuccess);
        var saves = harness.Store.SaveCount;
        Assert.True((await markRead.ExecuteAsync(1, CancellationToken.None)).IsSuccess);

        Assert.Equal(saves, harness.Store.SaveCount);
        Assert.Equal(1, (await count.ExecuteAsync(CancellationToken.None)).Value);
        Assert.Equal(harness.Clock.GetUtcNow(), harness.Store.Saved[0].ReadAt);
    }

    [Fact]
    public async Task Another_users_item_or_an_unknown_id_is_not_found()
    {
        var harness = await HarnessWith(count: 1);
        await harness.Writer().WriteAsync(NotificationsHarness.Test(NotificationChannels.InApp) with { UserId = 7 }, CancellationToken.None);
        var markRead = new MarkNotificationRead(harness.Owners(), harness.Store, harness.Clock);

        Assert.Equal(NotificationsErrorCodes.NotificationNotFound, (await markRead.ExecuteAsync(2, CancellationToken.None)).Refusal!.Code);
        Assert.Equal(NotificationsErrorCodes.NotificationNotFound, (await markRead.ExecuteAsync(99, CancellationToken.None)).Refusal!.Code);
        Assert.True(harness.Store.Saved[1].IsUnread);
    }

    [Fact]
    public async Task A_caller_without_an_account_sees_an_empty_feed_and_finds_nothing()
    {
        var harness = await HarnessWith(count: 1);
        harness.CurrentUser.SubjectId = Guid.NewGuid();

        var page = (await new GetNotificationFeed(harness.Owners(), harness.Store, harness.Language, harness.RuntimeSettings).ExecuteAsync(new FeedRequest(null, null), CancellationToken.None)).Value;
        var count = (await new GetUnreadNotificationCount(harness.Owners(), harness.Store).ExecuteAsync(CancellationToken.None)).Value;
        var read = await new MarkNotificationRead(harness.Owners(), harness.Store, harness.Clock).ExecuteAsync(1, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(0, count);
        Assert.Equal(NotificationsErrorCodes.NotificationNotFound, read.Refusal!.Code);
        harness.CurrentUser.SubjectId = null;
        Assert.Null(await harness.Owners().FindAsync(CancellationToken.None));
    }

    private static async Task<NotificationsHarness> HarnessWith(int count)
    {
        var harness = new NotificationsHarness();
        for (var index = 0; index < count; index++)
        {
            await harness.Writer().WriteAsync(NotificationsHarness.Test(NotificationChannels.InApp), CancellationToken.None);
        }

        return harness;
    }
}

public sealed class SendTestNotificationTests
{
    [Fact]
    public async Task The_caller_gets_the_bell_item_and_queued_email_and_sms()
    {
        var harness = new NotificationsHarness();
        var useCase = new SendTestNotification(harness.Owners(), harness.Writer(), harness.Clock);

        Assert.True((await useCase.ExecuteAsync(CancellationToken.None)).IsSuccess);

        var row = Assert.Single(harness.Store.Saved);
        Assert.Equal(NotificationsHarness.UserId, row.UserId);
        Assert.Equal(NotificationTemplateKeys.TestNotification, row.TemplateKey);
        Assert.Contains("2026-11-06 09:30 UTC", row.BodyAr, StringComparison.Ordinal);
        Assert.Equal([OutboxMessageTypes.NotificationEmail, OutboxMessageTypes.NotificationSms], harness.Outbox.Messages.Select(message => message.Type));
    }

    [Fact]
    public async Task Sms_is_left_out_while_disabled()
    {
        var harness = new NotificationsHarness(smsEnabled: false);

        await new SendTestNotification(harness.Owners(), harness.Writer(), harness.Clock).ExecuteAsync(CancellationToken.None);

        Assert.Equal(ChannelDeliveryState.Disabled, Assert.Single(harness.Store.Saved).SmsState);
        Assert.Single(harness.Outbox.Messages);
    }

    [Fact]
    public async Task A_caller_without_an_account_is_refused()
    {
        var harness = new NotificationsHarness();
        harness.CurrentUser.SubjectId = Guid.NewGuid();

        var outcome = await new SendTestNotification(harness.Owners(), harness.Writer(), harness.Clock).ExecuteAsync(CancellationToken.None);

        Assert.Equal(NotificationsErrorCodes.RecipientNotFound, outcome.Refusal!.Code);
        Assert.Empty(harness.Store.Saved);
    }
}

public sealed class NotificationDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Completing_a_channel_accepts_sent_or_no_address_only_and_only_outbox_channels()
    {
        var notification = Notification.ForAddress(new AddressedDraft("a@example.test", AppLanguage.English, "k", "{}", Now));

        Assert.Throws<ArgumentOutOfRangeException>(() => notification.Complete(NotificationChannels.Email, ChannelDeliveryState.Pending, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => notification.Complete(NotificationChannels.InApp, ChannelDeliveryState.Sent, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => notification.StateOf(NotificationChannels.InApp));

        notification.Complete(NotificationChannels.Email, ChannelDeliveryState.NoAddress, Now);
        Assert.Equal(ChannelDeliveryState.NoAddress, notification.EmailState);
        Assert.Null(notification.EmailSentAt);
        Assert.Null(notification.PlaceholdersJson);
    }

    [Fact]
    public void Channel_selection_rules()
    {
        Assert.True(NotificationChannelSet.IsValidSelection(NotificationChannels.InApp | NotificationChannels.Sms));
        Assert.False(NotificationChannelSet.IsValidSelection(NotificationChannels.None));
        Assert.False(NotificationChannelSet.IsSingle(NotificationChannels.InApp | NotificationChannels.Email));
        Assert.Equal([NotificationChannels.InApp, NotificationChannels.Sms], NotificationChannelSet.Split(NotificationChannels.Sms | NotificationChannels.InApp));
    }
}

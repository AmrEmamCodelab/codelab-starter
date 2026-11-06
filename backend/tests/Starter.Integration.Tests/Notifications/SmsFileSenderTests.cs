using System.Net;
using Starter.Application.Notifications;
using Starter.Domain.Localization;
using Starter.Integration.Tests.Support;

namespace Starter.Integration.Tests.Notifications;

/// <summary>
/// Exit criterion: SMS through the file SMS sender records a bilingual message. SMS is enabled on this host
/// (<c>Notifications:Sms:Enabled</c>, file mode). Accounts store no mobile number or language yet, so the recipients
/// directory answers two recipients through the test override: an Arabic one and an English one, each with its own
/// mobile number. The test notification to each records one SMS in that recipient's language.
/// </summary>
[Collection(Phase5HostGroup.Name)]
public sealed class SmsFileSenderTests(Phase5Host host)
{
    private const string TestNotification = "/api/admin/notifications/test";
    private const string MobilePrefix = "+9665";

    [Fact]
    public async Task The_test_notification_records_an_Arabic_and_an_English_SMS_for_recipients_in_those_languages()
    {
        var arabicMobile = MobilePrefix + TestNames.Suffix();
        var englishMobile = MobilePrefix + TestNames.Suffix();

        await SendTestNotificationAsync(new RecipientOverride(Email: null, arabicMobile, AppLanguage.Arabic));
        await SendTestNotificationAsync(new RecipientOverride(Email: null, englishMobile, AppLanguage.English));

        await AssertRecordedAsync(arabicMobile, AppLanguage.Arabic);
        await AssertRecordedAsync(englishMobile, AppLanguage.English);
    }

    /// <summary>Sends the test notification and drains the outbox while <paramref name="recipient"/> is in force (the address is read at delivery).</summary>
    private async Task SendTestNotificationAsync(RecipientOverride recipient)
    {
        using var scope = host.Recipients.Use(recipient);
        using var api = host.CreateApi();
        using var accepted = await api.PostAsync(TestNotification, body: null, await host.AdminTokenAsync());
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        await host.DrainOutboxAsync();
    }

    private async Task AssertRecordedAsync(string mobile, AppLanguage language)
    {
        var text = await NotificationTemplates.TestNotificationAsync(host.DatabaseName, NotificationChannels.Sms, language);
        var sms = Assert.Single(host.Sms.To(mobile));
        Assert.Equal((int)language, sms.Language);
        Assert.StartsWith(text.BodyBefore(TemplateText.SentAtPlaceholder), sms.Text, StringComparison.Ordinal);
        Assert.EndsWith(text.BodyAfter(TemplateText.SentAtPlaceholder), sms.Text, StringComparison.Ordinal);
    }
}

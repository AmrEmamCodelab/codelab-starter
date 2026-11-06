using System.Net;
using System.Text.Json;
using Starter.Application.Notifications;
using Starter.Domain.Localization;
using Starter.Infrastructure.Localization;
using Starter.Integration.Tests.Support;

namespace Starter.Integration.Tests.Notifications;

/// <summary>
/// The administrator's test notification on real SQL: the bell feed shows the seeded template wording in the request
/// language (Arabic and English), marking it read works, and the e-mail channel drops one <c>.eml</c> in the
/// recipient's language after the outbox drain (the file-drop stand-in for an SMTP sink).
/// </summary>
[Collection(Phase5HostGroup.Name)]
public sealed class TestNotificationTests(Phase5Host host)
{
    private const string TestNotification = "/api/admin/notifications/test";
    private const string NewestNotification = "/api/notifications?take=1";
    private const string UnreadCount = "/api/notifications/unread-count";

    [Fact]
    public async Task The_bell_shows_the_template_wording_per_language_and_the_email_is_dropped_in_the_recipients_language()
    {
        var mailbox = $"bell.{TestNames.Suffix()}@{PiiCanaries.EmailDomain}";
        using var recipient = host.Recipients.Use(new RecipientOverride(mailbox, Mobile: null, AppLanguage.English));
        var adminToken = await host.AdminTokenAsync();
        using var api = host.CreateApi();

        using (var accepted = await api.PostAsync(TestNotification, body: null, adminToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }

        var arabic = await NewestAsync(api, adminToken, LanguageTags.Arabic);
        var english = await NewestAsync(api, adminToken, LanguageTags.English);
        await AssertWordingAsync(arabic, AppLanguage.Arabic);
        await AssertWordingAsync(english, AppLanguage.English);
        Assert.Equal(arabic.GetProperty("id").GetString(), english.GetProperty("id").GetString()); // one item, two renderings
        Assert.False(arabic.GetProperty("read").GetBoolean());

        await host.DrainOutboxAsync();

        var mail = Assert.Single(host.Mail.To(mailbox));
        var emailText = await NotificationTemplates.TestNotificationAsync(host.DatabaseName, NotificationChannels.Email, AppLanguage.English);
        Assert.Equal(emailText.Subject, mail.Subject);
        Assert.Contains(emailText.BodyBefore(TemplateText.SentAtPlaceholder), mail.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(TemplateText.SentAtPlaceholder, mail.Body, StringComparison.Ordinal);
        await AssertMarkReadAsync(api, arabic.GetProperty("id").GetString()!, adminToken);
    }

    private async Task AssertWordingAsync(JsonElement item, AppLanguage language)
    {
        var text = await NotificationTemplates.TestNotificationAsync(host.DatabaseName, NotificationChannels.InApp, language);
        var body = item.GetProperty("bodyText").GetString()!;
        Assert.Equal(text.Subject, item.GetProperty("titleText").GetString());
        Assert.StartsWith(text.BodyBefore(TemplateText.SentAtPlaceholder), body, StringComparison.Ordinal);
        Assert.EndsWith(text.BodyAfter(TemplateText.SentAtPlaceholder), body, StringComparison.Ordinal);
        Assert.DoesNotContain(TemplateText.SentAtPlaceholder, body, StringComparison.Ordinal);
    }

    private static async Task<JsonElement> NewestAsync(ApiClient api, string accessToken, string language)
    {
        using var response = await api.GetAsync(NewestNotification, accessToken, language);
        var page = await ApiClient.ReadAsync(response, HttpStatusCode.OK);
        return page.GetProperty("items").EnumerateArray().First();
    }

    private static async Task AssertMarkReadAsync(ApiClient api, string id, string accessToken)
    {
        using var before = await api.GetAsync(UnreadCount, accessToken);
        var unread = (await ApiClient.ReadAsync(before, HttpStatusCode.OK)).GetProperty("count").GetInt32();
        using var marked = await api.PostAsync($"/api/notifications/{id}/read", body: null, accessToken);
        using var after = await api.GetAsync(UnreadCount, accessToken);

        Assert.Equal(HttpStatusCode.NoContent, marked.StatusCode);
        Assert.Equal(unread - 1, (await ApiClient.ReadAsync(after, HttpStatusCode.OK)).GetProperty("count").GetInt32());
    }
}

using Starter.Application.Notifications;
using Starter.Domain.Localization;
using Starter.Modules.Notifications.Application;
using Starter.Modules.Notifications.Application.Rendering;
using Starter.Modules.Notifications.Domain;
using Starter.Modules.Notifications.Infrastructure.Persistence.Migrations;
using Starter.Modules.Notifications.Tests.Fakes;

namespace Starter.Modules.Notifications.Tests.Application;

public sealed class TemplateRendererTests
{
    private static readonly Dictionary<string, string> Name = new(StringComparer.Ordinal) { ["name"] = "Sara" };

    [Fact]
    public void Placeholders_are_replaced_in_Arabic_and_English()
    {
        Assert.Equal("مرحباً Sara", TemplateRenderer.Render("مرحباً {{name}}", Name, TextEncoding.PlainText).Value);
        Assert.Equal("Hello Sara, Sara", TemplateRenderer.Render("Hello {{name}}, {{name}}", Name, TextEncoding.PlainText).Value);
    }

    [Fact]
    public void A_missing_placeholder_refuses_the_whole_text()
    {
        var outcome = TemplateRenderer.Render("Hello {{name}} {{other}}", Name, TextEncoding.PlainText);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(NotificationsErrorCodes.PlaceholderMissing, outcome.Refusal.Code);
        Assert.Equal(["other"], TemplateRenderer.MissingPlaceholders("{{other}} {{name}} {{other}}", Name));
    }

    [Fact]
    public void Placeholder_names_are_case_sensitive()
    {
        Assert.False(TemplateRenderer.Render("{{Name}}", Name, TextEncoding.PlainText).IsSuccess);
    }

    [Fact]
    public void Html_encoding_turns_markup_in_a_value_into_text()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "<script>x</script> & \"q\"" };

        var html = TemplateRenderer.Render("<p>{{name}}</p>", values, TextEncoding.Html).Value;

        Assert.Equal("<p>&lt;script&gt;x&lt;/script&gt; &amp; &quot;q&quot;</p>", html);
    }

    [Fact]
    public void Single_line_encoding_removes_line_breaks()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "a\r\nBcc: x\nb" };

        Assert.Equal("Subject a Bcc: x b", TemplateRenderer.Render("Subject {{name}}", values, TextEncoding.SingleLine).Value);
    }

    [Fact]
    public void Text_that_only_looks_like_a_token_is_left_alone_and_unused_values_are_ignored()
    {
        var values = new Dictionary<string, string>(Name, StringComparer.Ordinal) { ["unused"] = "x" };

        Assert.Equal("{ name } {{1x}} Sara", TemplateRenderer.Render("{ name } {{1x}} {{name}}", values, TextEncoding.PlainText).Value);
    }
}

public sealed class NotificationComposerTests
{
    [Fact]
    public async Task Each_channel_gets_its_encoding_and_language()
    {
        var composer = new NotificationComposer(new FakeTemplateStore());
        var slots = NotificationComposer.SlotsFor(NotificationChannelSet.All);

        var outcome = await composer.ComposeAsync(
            NotificationTemplateKeys.TestNotification, NotificationsHarness.SentAt("<b>now</b>"), slots, CancellationToken.None);

        var rendered = outcome.Value;
        Assert.Equal(6, rendered.Count);
        Assert.Contains("&lt;b&gt;now&lt;/b&gt;", rendered[new TemplateSlot(NotificationChannels.Email, AppLanguage.English)].Body, StringComparison.Ordinal);
        Assert.Contains("<b>now</b>", rendered[new TemplateSlot(NotificationChannels.InApp, AppLanguage.English)].Body, StringComparison.Ordinal);
        Assert.Equal("إشعار تجريبي", rendered[new TemplateSlot(NotificationChannels.InApp, AppLanguage.Arabic)].Subject);
        Assert.Equal(string.Empty, rendered[new TemplateSlot(NotificationChannels.Sms, AppLanguage.Arabic)].Subject);
    }

    [Fact]
    public async Task A_missing_text_refuses_with_template_not_found()
    {
        var composer = new NotificationComposer(new FakeTemplateStore());

        var outcome = await composer.ComposeOneAsync(
            NotificationTemplateKeys.AccountActivation, new Dictionary<string, string>(), new TemplateSlot(NotificationChannels.Sms, AppLanguage.Arabic), CancellationToken.None);

        Assert.Equal(NotificationsErrorCodes.TemplateNotFound, outcome.Refusal!.Code);
    }

    [Fact]
    public async Task An_unknown_key_refuses_with_template_not_found()
    {
        var composer = new NotificationComposer(new FakeTemplateStore());

        var outcome = await composer.ComposeOneAsync(
            "no-such-key", new Dictionary<string, string>(), new TemplateSlot(NotificationChannels.Email, AppLanguage.English), CancellationToken.None);

        Assert.Equal(NotificationsErrorCodes.TemplateNotFound, outcome.Refusal!.Code);
    }
}

public sealed class TemplateSeedTests
{
    public static TheoryData<string, string[]> DocumentedPlaceholders => new()
    {
        { NotificationTemplateKeys.AccountActivation, [NotificationPlaceholders.ActivationLink, NotificationPlaceholders.ExpiresAt] },
        {
            NotificationTemplateKeys.ContactReceived,
            [
                NotificationPlaceholders.ReferenceNumber, NotificationPlaceholders.FullName, NotificationPlaceholders.Email,
                NotificationPlaceholders.Topic, NotificationPlaceholders.Message,
            ]
        },
        { NotificationTemplateKeys.TestNotification, [NotificationPlaceholders.SentAt] },
    };

    [Theory]
    [MemberData(nameof(DocumentedPlaceholders))]
    public void Every_text_of_a_key_uses_exactly_its_documented_placeholders(string key, string[] placeholders)
    {
        var texts = TemplateSeed.Rows.Where(row => row.Key == key).ToList();
        var used = texts.SelectMany(text => TemplateRenderer.MissingPlaceholders((text.Subject ?? string.Empty) + text.Body, new Dictionary<string, string>()))
            .Distinct(StringComparer.Ordinal);

        Assert.NotEmpty(texts);
        Assert.Equal(placeholders.Order(StringComparer.Ordinal), used.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_key_and_channel_has_Arabic_and_English_and_rows_are_unique()
    {
        var rows = TemplateSeed.Rows.Select(NotificationTemplate.Define).ToList();

        Assert.Equal(rows.Count, rows.Select(row => (row.Key, row.Channel, row.Language)).Distinct().Count());
        foreach (var group in rows.GroupBy(row => (row.Key, row.Channel)))
        {
            Assert.Equal([AppLanguage.Arabic, AppLanguage.English], group.Select(row => row.Language).Order());
        }

        Assert.All(rows, row => Assert.True(row.Key.Length <= NotificationsLimits.TemplateKeyMaxLength));
        Assert.All(rows, row => Assert.True((row.Subject ?? string.Empty).Length <= NotificationsLimits.SubjectMaxLength));
        Assert.All(rows.Where(row => row.Channel != NotificationChannels.Sms), row => Assert.False(string.IsNullOrWhiteSpace(row.Subject)));
    }

    [Fact]
    public void A_text_with_more_than_one_channel_is_refused()
    {
        var text = new TemplateText("k", NotificationChannels.InApp | NotificationChannels.Email, AppLanguage.Arabic, "s", "b");

        Assert.Throws<ArgumentException>(() => NotificationTemplate.Define(text));
    }
}

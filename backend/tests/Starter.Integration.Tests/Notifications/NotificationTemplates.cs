using Starter.Application.Notifications;
using Starter.Domain.Localization;
using Starter.Integration.Tests.Support;

namespace Starter.Integration.Tests.Notifications;

/// <summary>One seeded template text, read from <c>notifications.NotificationTemplates</c> (the database, not the seed code).</summary>
/// <param name="Subject">The title or subject, or empty.</param>
/// <param name="Body">The body with its <c>{{placeholders}}</c>.</param>
internal sealed record TemplateText(string Subject, string Body)
{
    /// <summary>The placeholder of the test notification's send time.</summary>
    public const string SentAtPlaceholder = "{{sentAt}}";

    /// <summary>The body text before <paramref name="placeholder"/>.</summary>
    public string BodyBefore(string placeholder) => Body[..Body.IndexOf(placeholder, StringComparison.Ordinal)];

    /// <summary>The body text after <paramref name="placeholder"/>.</summary>
    public string BodyAfter(string placeholder) => Body[(Body.IndexOf(placeholder, StringComparison.Ordinal) + placeholder.Length)..];
}

/// <summary>Reads the seeded template texts of a host's database.</summary>
internal static class NotificationTemplates
{
    private const char Separator = '\u001F';
    private const int SubjectPart = 0;
    private const int BodyPart = 1;

    private const string TextOf =
        "SELECT CONCAT(ISNULL([Subject], N''), NCHAR(31), [Body]) FROM [notifications].[NotificationTemplates] "
        + "WHERE [Key] = @key AND [Channel] = @channel AND [Language] = @language";

    /// <summary>The text of <paramref name="key"/> on <paramref name="channel"/> in <paramref name="language"/>.</summary>
    public static async Task<TemplateText> ReadAsync(string database, string key, NotificationChannels channel, AppLanguage language)
    {
        var value = await TestSqlServer.ScalarAsync(
            database,
            TextOf,
            new Dictionary<string, object> { ["@key"] = key, ["@channel"] = (int)channel, ["@language"] = (int)language });
        var parts = Assert.IsType<string>(value).Split(Separator);
        return new TemplateText(parts[SubjectPart], parts[BodyPart]);
    }

    /// <summary>The test notification's texts.</summary>
    public static Task<TemplateText> TestNotificationAsync(string database, NotificationChannels channel, AppLanguage language) =>
        ReadAsync(database, NotificationTemplateKeys.TestNotification, channel, language);
}

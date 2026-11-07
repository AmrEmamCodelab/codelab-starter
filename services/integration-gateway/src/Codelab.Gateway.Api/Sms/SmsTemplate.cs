using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Codelab.Gateway.Api.Sms;

/// <summary>The values a template's placeholders stand for, already in the provider's terms.</summary>
/// <param name="To">The recipient.</param>
/// <param name="Text">The message text.</param>
/// <param name="Language">The provider's language code.</param>
/// <param name="Sender">The sender id (empty when none is configured).</param>
internal sealed record SmsTemplateValues(string To, string Text, string Language, string Sender);

/// <summary>
/// Renders the provider templates. Every placeholder is replaced in ONE pass over the template, so a value that itself
/// contains <c>{to}</c> or <c>{sender}</c> is never substituted again; each value is encoded for where it lands
/// (percent-encoding in a URL or form, JSON escaping in a JSON body). JSON escaping is the relaxed one: quotes,
/// backslashes and control characters are escaped, Arabic text and <c>+</c> stay as they are, because the body goes to
/// a provider API, never into an HTML page.
/// </summary>
internal static partial class SmsTemplate
{
    /// <summary>The recipient placeholder.</summary>
    public const string To = "{to}";

    /// <summary>The message text placeholder.</summary>
    public const string Text = "{text}";

    /// <summary>The language code placeholder.</summary>
    public const string Language = "{language}";

    /// <summary>The sender id placeholder.</summary>
    public const string Sender = "{sender}";

    /// <summary>Every placeholder.</summary>
    public static IReadOnlyList<string> Placeholders { get; } = [To, Text, Language, Sender];

    /// <summary>Renders a URL template (values percent-encoded).</summary>
    public static string RenderUrl(string template, SmsTemplateValues values) => Render(template, values, Uri.EscapeDataString);

    /// <summary>Renders a body template in <paramref name="format"/>.</summary>
    public static string RenderBody(string template, SmsTemplateValues values, SmsBodyFormat format) =>
        Render(template, values, format == SmsBodyFormat.Json ? EscapeJson : Uri.EscapeDataString);

    /// <summary>The placeholders in <paramref name="template"/> that are not one of <see cref="Placeholders"/>.</summary>
    public static IReadOnlyList<string> UnknownPlaceholders(string? template) =>
        string.IsNullOrEmpty(template)
            ? []
            : [.. AnyPlaceholder().Matches(template).Select(match => match.Value).Where(value => !Placeholders.Contains(value, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal)];

    private static string Render(string template, SmsTemplateValues values, Func<string, string> encode)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);
        return KnownPlaceholder().Replace(template, match => encode(ValueOf(match.Value, values)));
    }

    private static string ValueOf(string placeholder, SmsTemplateValues values) => placeholder switch
    {
        To => values.To,
        Text => values.Text,
        Language => values.Language,
        Sender => values.Sender,
        _ => throw new ArgumentOutOfRangeException(nameof(placeholder), placeholder, "Not an SMS template placeholder."),
    };

    private static string EscapeJson(string value) => JsonEncodedText.Encode(value, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString();

    [GeneratedRegex(@"\{(?:to|text|language|sender)\}", RegexOptions.CultureInvariant)]
    private static partial Regex KnownPlaceholder();

    [GeneratedRegex(@"\{[A-Za-z_][A-Za-z0-9_]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex AnyPlaceholder();
}

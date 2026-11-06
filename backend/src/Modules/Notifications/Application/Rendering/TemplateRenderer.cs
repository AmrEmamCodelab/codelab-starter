using System.Net;
using System.Text.RegularExpressions;
using Starter.Application.Results;

namespace Starter.Modules.Notifications.Application.Rendering;

/// <summary>The placeholder syntax of template texts: <c>{{name}}</c>, a letter then letters or digits, case-sensitive.</summary>
public static partial class TemplateTokens
{
    /// <summary>Opens a placeholder.</summary>
    public const string Open = "{{";

    /// <summary>Closes a placeholder.</summary>
    public const string Close = "}}";

    /// <summary>The placeholder pattern; group <see cref="NameGroup"/> is the name.</summary>
    public const string Pattern = @"\{\{(?<name>[A-Za-z][A-Za-z0-9]*)\}\}";

    /// <summary>The regex group that holds the placeholder name.</summary>
    public const string NameGroup = "name";

    /// <summary>The placeholder text of <paramref name="name"/> (for tests and seeds).</summary>
    public static string Of(string name) => Open + name + Close;

    /// <summary>The compiled <see cref="Pattern"/>.</summary>
    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    public static partial Regex Placeholder();
}

/// <summary>How a placeholder value is written into the text.</summary>
public enum TextEncoding
{
    /// <summary>As is (bell and SMS bodies; the web app escapes on display).</summary>
    PlainText = 1,

    /// <summary>As is, with line breaks turned into spaces (subjects and titles: one line, no header injection).</summary>
    SingleLine = 2,

    /// <summary>HTML-encoded (e-mail bodies), so a value can never add markup or a link.</summary>
    Html = 3,
}

/// <summary>
/// Pure placeholder substitution. Every <c>{{name}}</c> in the text is replaced by its encoded value; a placeholder
/// without a value refuses the whole text (<see cref="NotificationsErrorCodes.PlaceholderMissing"/>), so a raw token is
/// never sent. Values the text does not use are ignored. The template text itself is trusted (seeded by migration).
/// </summary>
public static class TemplateRenderer
{
    private const char Space = ' ';
    private static readonly char[] LineBreaks = ['\r', '\n'];

    /// <summary>The rendered text, or the missing-placeholder refusal.</summary>
    public static Outcome<string> Render(string text, IReadOnlyDictionary<string, string> values, TextEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(values);
        if (MissingPlaceholders(text, values).Count > 0)
        {
            return Refusal.Unprocessable(NotificationsErrorCodes.PlaceholderMissing);
        }

        return TemplateTokens.Placeholder().Replace(text, match => Encode(values[match.Groups[TemplateTokens.NameGroup].Value], encoding));
    }

    /// <summary>The placeholder names <paramref name="text"/> uses that have no value, in order of first use.</summary>
    public static IReadOnlyList<string> MissingPlaceholders(string text, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(values);
        return TemplateTokens.Placeholder().Matches(text)
            .Select(match => match.Groups[TemplateTokens.NameGroup].Value)
            .Where(name => !values.ContainsKey(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary><paramref name="value"/> written for <paramref name="encoding"/>.</summary>
    public static string Encode(string? value, TextEncoding encoding)
    {
        var text = value ?? string.Empty;
        return encoding switch
        {
            TextEncoding.PlainText => text,
            TextEncoding.SingleLine => OneLine(text),
            TextEncoding.Html => WebUtility.HtmlEncode(text),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unknown text encoding."),
        };
    }

    private static string OneLine(string text) =>
        text.IndexOfAny(LineBreaks) < 0 ? text : string.Join(Space, text.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries));
}

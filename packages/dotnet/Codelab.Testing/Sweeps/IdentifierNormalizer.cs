using System.Text;

namespace Codelab.Testing.Sweeps;

/// <summary>
/// The normalized view of a line that term rules also match, so a term hidden inside an identifier or written with
/// Arabic diacritics is still found (same rules as the repository's scrub sweep): identifiers split on case, digit and
/// <c>_</c> boundaries (<c>OrderId</c> → <c>Order Id</c>), Arabic tashkeel and tatweel removed, alef forms unified and
/// alef maqsura read as ya.
/// </summary>
public static class IdentifierNormalizer
{
    private const char Space = ' ';
    private const char Underscore = '_';
    private const char Tatweel = 'ـ';
    private const char SuperscriptAlef = 'ٰ';
    private const char PlainAlef = 'ا';
    private const char AlefMaqsura = 'ى';
    private const char Ya = 'ي';
    private const char TashkeelFirst = 'ً';
    private const char TashkeelLast = 'ٟ';
    private const char QuranicMarkFirst = 'ۖ';
    private const char QuranicMarkLast = 'ۭ';
    private static readonly char[] AlefForms = ['أ', 'إ', 'آ', 'ٱ'];

    /// <summary>The normalized text of <paramref name="line"/>.</summary>
    public static string Normalize(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var text = new StringBuilder(line.Length);
        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            if (IsDiacritic(current))
            {
                continue;
            }

            if (IsCaseBoundary(line, index))
            {
                text.Append(Space);
            }

            text.Append(current == Underscore ? Space : NormalizeArabic(current));
        }

        return text.ToString();
    }

    private static bool IsCaseBoundary(string line, int index)
    {
        if (index == 0)
        {
            return false;
        }

        var previous = line[index - 1];
        var current = line[index];
        var next = index + 1 < line.Length ? line[index + 1] : (char?)null;
        return IsCaseChange(previous, current, next) || IsDigitBoundary(previous, current);
    }

    // lowerUpper (isSubmitted) or the end of an acronym before a word (IApiClient -> I Api).
    private static bool IsCaseChange(char previous, char current, char? next)
    {
        var lowerThenUpper = char.IsLower(previous) && char.IsUpper(current);
        var acronymEnd = char.IsUpper(previous) && char.IsUpper(current) && next is { } following && char.IsLower(following);
        return lowerThenUpper || acronymEnd;
    }

    private static bool IsDigitBoundary(char previous, char current) =>
        (char.IsLetter(previous) && char.IsDigit(current)) || (char.IsDigit(previous) && char.IsLetter(current));

    private static bool IsDiacritic(char value) =>
        value is Tatweel or SuperscriptAlef
        || (value >= TashkeelFirst && value <= TashkeelLast)
        || (value >= QuranicMarkFirst && value <= QuranicMarkLast);

    private static char NormalizeArabic(char value)
    {
        if (Array.IndexOf(AlefForms, value) >= 0)
        {
            return PlainAlef;
        }

        return value == AlefMaqsura ? Ya : value;
    }
}

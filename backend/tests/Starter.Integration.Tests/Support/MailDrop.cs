using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Starter.Integration.Tests.Support;

/// <summary>One e-mail the file-drop sender wrote: the recipient, the decoded subject and the decoded body.</summary>
/// <param name="To">The recipient address (the <c>To</c> header without a display name).</param>
/// <param name="Subject">The subject, RFC 2047 encoded words decoded.</param>
/// <param name="Body">The body, transfer encoding (base64 or quoted-printable) decoded as UTF-8.</param>
/// <param name="FileName">The <c>.eml</c> file it was read from.</param>
public sealed record DroppedMail(string To, string Subject, string Body, string FileName)
{
    /// <summary>The first <c>href</c> of the body, HTML-decoded, or null when the body has no link.</summary>
    public string? FirstLink => EmlReader.FirstLink(Body);
}

/// <summary>
/// The <c>.eml</c> files a test host's file-drop e-mail sender writes into its pickup folder (the stand-in for an SMTP
/// sink in integration tests).
/// </summary>
/// <param name="directory">The pickup folder (it may not exist yet: no mail was sent).</param>
public sealed class MailDrop(string directory)
{
    private const string MailPattern = "*.eml";

    /// <summary>The pickup folder.</summary>
    public string Directory { get; } = directory;

    /// <summary>Every dropped mail, oldest file first.</summary>
    public IReadOnlyList<DroppedMail> ReadAll()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        return new DirectoryInfo(Directory)
            .GetFiles(MailPattern)
            .OrderBy(file => file.CreationTimeUtc)
            .ThenBy(file => file.Name, StringComparer.Ordinal)
            .Select(file => EmlReader.Parse(File.ReadAllText(file.FullName, Encoding.ASCII), file.Name))
            .ToList();
    }

    /// <summary>Every dropped mail to <paramref name="address"/> (case-insensitive), oldest first.</summary>
    public IReadOnlyList<DroppedMail> To(string address) =>
        ReadAll().Where(mail => string.Equals(mail.To, address, StringComparison.OrdinalIgnoreCase)).ToList();
}

/// <summary>
/// A minimal reader for the single-part messages <c>System.Net.Mail</c> writes to a pickup folder: unfolded headers,
/// RFC 2047 encoded-word subjects (B and Q), and base64, quoted-printable, 7bit or 8bit bodies. Not a general MIME parser.
/// </summary>
public static partial class EmlReader
{
    private const string ToHeader = "To";
    private const string SubjectHeader = "Subject";
    private const string TransferEncodingHeader = "Content-Transfer-Encoding";
    private const string Base64Encoding = "base64";
    private const string QuotedPrintableEncoding = "quoted-printable";
    private const string HeaderBodySeparator = "\r\n\r\n";
    private const string LineBreak = "\r\n";
    private const char HeaderNameSeparator = ':';
    private const string SoftLineBreak = "=\r\n";
    private const char EncodedOctetMarker = '=';
    private const int HexRadix = 16;
    private const int HexPairLength = 2;
    private const string Base64Flag = "B";
    private const char QEncodingSpace = '_';
    private const string AdjacentWordsJoined = "?==?";

    /// <summary>Parses one <c>.eml</c> text.</summary>
    /// <exception cref="FormatException">The text has no header/body separator or no <c>To</c> header.</exception>
    public static DroppedMail Parse(string eml, string fileName)
    {
        ArgumentNullException.ThrowIfNull(eml);
        var text = eml.ReplaceLineEndings(LineBreak);
        var split = text.IndexOf(HeaderBodySeparator, StringComparison.Ordinal);
        if (split < 0)
        {
            throw new FormatException("The message has no blank line between the headers and the body.");
        }

        var headers = Headers(text[..split]);
        var body = text[(split + HeaderBodySeparator.Length)..];
        var to = headers.TryGetValue(ToHeader, out var toValue) ? Address(toValue) : throw new FormatException("The message has no To header.");
        var subject = headers.TryGetValue(SubjectHeader, out var subjectValue) ? DecodeWords(subjectValue) : string.Empty;
        var encoding = headers.TryGetValue(TransferEncodingHeader, out var encodingValue) ? encodingValue.Trim() : string.Empty;
        return new DroppedMail(to, subject, DecodeBody(body, encoding), fileName);
    }

    /// <summary>The first <c>href="..."</c> value of <paramref name="html"/>, HTML-decoded, or null.</summary>
    public static string? FirstLink(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var match = HrefPattern().Match(html);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["href"].Value) : null;
    }

    /// <summary>Headers with folded lines joined; a repeated header keeps its first value.</summary>
    public static IReadOnlyDictionary<string, string> Headers(string headerBlock)
    {
        ArgumentNullException.ThrowIfNull(headerBlock);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? name = null;
        foreach (var line in headerBlock.Split(LineBreak))
        {
            if (line.Length > 0 && char.IsWhiteSpace(line[0]) && name is not null)
            {
                headers[name] += line;
                continue;
            }

            var colon = line.IndexOf(HeaderNameSeparator, StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            name = line[..colon].Trim();
            if (!headers.TryAdd(name, line[(colon + 1)..].Trim()))
            {
                name = null;
            }
        }

        return headers;
    }

    /// <summary>Decodes RFC 2047 encoded words; the white space between two adjacent encoded words is dropped.</summary>
    public static string DecodeWords(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var joined = AdjacentWordsGap().Replace(value, AdjacentWordsJoined);
        return EncodedWord().Replace(joined, match =>
        {
            var charset = Encoding.GetEncoding(match.Groups["charset"].Value);
            var payload = match.Groups["text"].Value;
            var bytes = string.Equals(match.Groups["flag"].Value, Base64Flag, StringComparison.OrdinalIgnoreCase)
                ? Convert.FromBase64String(payload)
                : QuotedPrintableBytes(payload.Replace(QEncodingSpace, ' '));
            return charset.GetString(bytes);
        });
    }

    /// <summary>Decodes a body in <paramref name="transferEncoding"/> as UTF-8 (anything else is returned as is).</summary>
    public static string DecodeBody(string body, string transferEncoding)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.Equals(transferEncoding, Base64Encoding, StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(string.Concat(body.Where(character => !char.IsWhiteSpace(character)))));
        }

        return string.Equals(transferEncoding, QuotedPrintableEncoding, StringComparison.OrdinalIgnoreCase)
            ? Encoding.UTF8.GetString(QuotedPrintableBytes(body.Replace(SoftLineBreak, string.Empty, StringComparison.Ordinal)))
            : body;
    }

    private static string Address(string headerValue)
    {
        var match = AngleAddress().Match(headerValue);
        return match.Success ? match.Groups["address"].Value : headerValue.Trim();
    }

    private static byte[] QuotedPrintableBytes(string text)
    {
        var bytes = new List<byte>(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == EncodedOctetMarker && IsHexPair(text, index + 1))
            {
                bytes.Add(Convert.ToByte(text.Substring(index + 1, HexPairLength), HexRadix));
                index += HexPairLength;
                continue;
            }

            bytes.AddRange(Encoding.UTF8.GetBytes(text[index].ToString()));
        }

        return [.. bytes];
    }

    private static bool IsHexPair(string text, int start) =>
        start + HexPairLength <= text.Length && Uri.IsHexDigit(text[start]) && Uri.IsHexDigit(text[start + 1]);

    [GeneratedRegex(@"=\?(?<charset>[^?]+)\?(?<flag>[BbQq])\?(?<text>[^?]*)\?=")]
    private static partial Regex EncodedWord();

    [GeneratedRegex(@"\?=\s+=\?")]
    private static partial Regex AdjacentWordsGap();

    [GeneratedRegex("<(?<address>[^>]+)>")]
    private static partial Regex AngleAddress();

    [GeneratedRegex("href=\"(?<href>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex HrefPattern();
}

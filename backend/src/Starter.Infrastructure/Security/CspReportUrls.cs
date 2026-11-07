using System.Text;

namespace Starter.Infrastructure.Security;

/// <summary>
/// Redacts a URL taken from a CSP report before it is logged. Query strings and fragments carry what a screen holds
/// (search terms, tokens, ids), and user info carries credentials, so a web URL keeps only its scheme, host, port and
/// path. Any other scheme (<c>data:</c>, <c>blob:</c>, an extension scheme) keeps only the scheme name, because its
/// body is content. A value that is not an absolute URL (<c>inline</c>, <c>eval</c>, a relative path) is cut at its
/// first <c>?</c> or <c>#</c>. Control characters are dropped and the result is at most
/// <see cref="CspReportContract.MaxFieldLength"/> characters.
/// </summary>
public static class CspReportUrls
{
    private static readonly HashSet<string> WebSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeWs, Uri.UriSchemeWss,
    };

    private static readonly char[] QueryOrFragment = ['?', '#'];

    /// <summary>The redacted form of <paramref name="value"/>; empty for a missing or blank value.</summary>
    public static string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var redacted = AbsoluteUrl(trimmed) is { } uri
            ? WebSchemes.Contains(uri.Scheme) ? uri.Scheme + Uri.SchemeDelimiter + uri.Authority + uri.AbsolutePath : uri.Scheme
            : CutAtQueryOrFragment(trimmed);
        return Clip(redacted, CspReportContract.MaxFieldLength);
    }

    /// <summary><paramref name="value"/> without control characters, cut to <paramref name="maxLength"/> characters.</summary>
    public static string Clip(string value, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var character in value.Where(character => !char.IsControl(character)))
        {
            if (builder.Length == maxLength)
            {
                break;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    // Uri.TryCreate also accepts a rooted path as a file URL on some platforms; a real absolute URL starts with its own scheme.
    private static Uri? AbsoluteUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && value.StartsWith(uri.Scheme + ":", StringComparison.OrdinalIgnoreCase)
            ? uri
            : null;

    private static string CutAtQueryOrFragment(string value)
    {
        var cut = value.IndexOfAny(QueryOrFragment);
        return cut < 0 ? value : value[..cut];
    }
}

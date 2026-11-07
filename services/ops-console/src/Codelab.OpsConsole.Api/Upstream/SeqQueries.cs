using System.Globalization;
using Codelab.Observability;

namespace Codelab.OpsConsole.Api.Upstream;

/// <summary>
/// The Seq HTTP API requests and UI links the console builds. The only value that enters a filter is a reference code
/// normalized by <c>ReferenceCodeInput</c> (hex digits and one separator), so no operator text reaches Seq.
/// </summary>
public static class SeqQueries
{
    /// <summary>Seq's events endpoint, relative to its base address.</summary>
    public const string EventsPath = "api/events";

    /// <summary>The UI route that opens a filtered event stream.</summary>
    public const string UiEventsFragment = "#/events?filter=";

    /// <summary>The header that carries the Seq API key.</summary>
    public const string ApiKeyHeader = "X-Seq-ApiKey";

    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    /// <summary>The Seq filter for every event of <paramref name="referenceCode"/>: <c>ReferenceCode = '9C1E5-B7A20'</c>.</summary>
    public static string ReferenceCodeFilter(string referenceCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceCode);
        if (referenceCode.Contains('\'', StringComparison.Ordinal))
        {
            throw new ArgumentException("A reference code never contains a quote; normalize it first.", nameof(referenceCode));
        }

        return $"{LogPropertyNames.ReferenceCode} = '{referenceCode}'";
    }

    /// <summary>
    /// <c>api/events</c> for <paramref name="filter"/>: at most <paramref name="count"/> events written since
    /// <paramref name="fromUtc"/>, with rendered messages.
    /// </summary>
    public static Uri EventsRequest(string filter, int count, DateTimeOffset fromUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filter);
        var query = string.Join(
            '&',
            "filter=" + Uri.EscapeDataString(filter),
            "count=" + count.ToString(CultureInfo.InvariantCulture),
            "render=true",
            "fromDateUtc=" + Uri.EscapeDataString(fromUtc.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture)));
        return new Uri(EventsPath + "?" + query, UriKind.Relative);
    }

    /// <summary>The Seq UI link that shows the same <paramref name="filter"/>.</summary>
    public static Uri UiLink(Uri baseAddress, string filter)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(filter);
        return new Uri(baseAddress.AbsoluteUri + UiEventsFragment + Uri.EscapeDataString(filter), UriKind.Absolute);
    }
}

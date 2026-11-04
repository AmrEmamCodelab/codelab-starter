using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Codelab.Observability;

/// <summary>
/// The correlation id that ties a deliberately uninformative error body to the full server-side log line.
/// <para>
/// <b>One id per request, owned by the server.</b> The id is a 32-lowercase-hex W3C trace id: the request's own
/// trace id when it has an Activity, so the log line, the span and an audit row share it. No caller chooses it: an
/// inbound <see cref="Header"/> is recorded as <see cref="LogPropertyNames.InboundCorrelationId"/> and never
/// adopted, which would otherwise let an internet caller plant log lines under another user's reference code.
/// </para>
/// </summary>
public static class CorrelationId
{
    /// <summary>The response header that echoes the id, and the outbound header an internal service receives.</summary>
    public const string Header = "X-Correlation-ID";

    /// <summary>The shortest id accepted from outside (an inbound header, a job parameter).</summary>
    public const int MinLength = 8;

    /// <summary>The longest id accepted from outside: long enough for a GUID in "D" form, short enough not to pad logs.</summary>
    public const int MaxLength = 64;

    private const string ItemKey = "__codelab_correlation_id";

    /// <summary>
    /// The id for this request, allocated once and cached on <see cref="HttpContext.Items"/>: the hosting Activity's
    /// trace id, else <see cref="Activity.Current"/>'s, else a random 32-hex id. Always 32 lowercase hex.
    /// </summary>
    public static string For(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.TryGetValue(ItemKey, out var cached) && cached is string existing)
        {
            return existing;
        }

        var id = TraceIdOf(context.Features.Get<IHttpActivityFeature>()?.Activity)
            ?? TraceIdOf(Activity.Current)
            ?? ActivityTraceId.CreateRandom().ToHexString();

        context.Items[ItemKey] = id;
        return id;
    }

    /// <summary>
    /// The id for whatever is running now: <see cref="For"/> inside a request; outside one, the innermost
    /// <see cref="CorrelationScope"/>, then the trace id of a W3C <see cref="Activity.Current"/>. Null when there is
    /// none: a caller records "no id" rather than inventing one that matches nothing.
    /// </summary>
    public static string? Current(HttpContext? context) =>
        context is not null ? For(context) : CorrelationScope.Current ?? TraceIdOf(Activity.Current);

    /// <summary>
    /// The caller's <see cref="Header"/>, kept only as a log property: returned when it is well formed and differs
    /// from <see cref="For"/>, otherwise null. A header sent twice is joined with ", " and so fails the charset.
    /// </summary>
    public static string? InboundFor(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var inbound = context.Request.Headers[Header].ToString();
        return IsWellFormed(inbound) && !string.Equals(inbound, For(context), StringComparison.Ordinal) ? inbound : null;
    }

    /// <summary>
    /// True for an id of <see cref="MinLength"/> to <see cref="MaxLength"/> characters of <c>[A-Za-z0-9-]</c>. An
    /// ALLOWLIST, not a scan for bad characters: it still covers a GUID, a W3C trace id and a ULID.
    /// </summary>
    public static bool IsWellFormed(string? candidate) =>
        candidate is { Length: >= MinLength and <= MaxLength } && candidate.All(IsAllowedCharacter);

    private static bool IsAllowedCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c == '-';

    /// <summary>A hierarchical-format Activity has no trace id, so it yields null.</summary>
    private static string? TraceIdOf(Activity? activity) =>
        activity is { IdFormat: ActivityIdFormat.W3C } ? activity.TraceId.ToHexString() : null;
}

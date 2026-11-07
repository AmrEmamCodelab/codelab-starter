namespace Codelab.OpsConsole.Application.ReferenceCodes;

/// <summary>The HTTP request a reference code belongs to: its method, path (no query) and response status.</summary>
public sealed record TracedRequest(string Method, string Path, int Status);

/// <summary>One log event of a request, as the log store returned it.</summary>
public sealed record LoggedEvent
{
    /// <summary>When it was written.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>The level name (<c>Information</c>, <c>Warning</c>, <c>Error</c>, ...).</summary>
    public required string Level { get; init; }

    /// <summary>The rendered message (already PII-masked when it was written).</summary>
    public required string Message { get; init; }

    /// <summary>The event's correlation id, when it carries one.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>The event's trace id, when it carries one.</summary>
    public string? TraceId { get; init; }

    /// <summary>The request, when this is the request completion line.</summary>
    public TracedRequest? Request { get; init; }
}

/// <summary>What the API's /api/ops/reference-codes/{code} knows about a reference code; every member is optional.</summary>
public sealed record ApiReferenceCodeFacts
{
    /// <summary>The full correlation id of the request.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>The W3C trace id of the request.</summary>
    public string? TraceId { get; init; }

    /// <summary>When the code was first recorded.</summary>
    public DateTimeOffset? FirstSeen { get; init; }

    /// <summary>The request the code belongs to.</summary>
    public TracedRequest? Request { get; init; }
}

/// <summary>
/// The answer of the log store to one search. <see cref="Available"/> is false when the store is not configured or did
/// not answer; the events are then empty and say nothing about whether the code exists.
/// </summary>
public sealed record LogSearchResult
{
    private LogSearchResult(bool available, IReadOnlyList<LoggedEvent> events, Uri? link)
    {
        Available = available;
        Events = events;
        Link = link;
    }

    /// <summary>The store answered.</summary>
    public bool Available { get; }

    /// <summary>The matching events (empty when <see cref="Available"/> is false).</summary>
    public IReadOnlyList<LoggedEvent> Events { get; }

    /// <summary>A link that opens the same search in the log store's UI, when the store is configured.</summary>
    public Uri? Link { get; }

    /// <summary>The store answered with <paramref name="events"/>.</summary>
    public static LogSearchResult Found(IReadOnlyList<LoggedEvent> events, Uri? link)
    {
        ArgumentNullException.ThrowIfNull(events);
        return new LogSearchResult(available: true, events, link);
    }

    /// <summary>The store is not configured or did not answer.</summary>
    public static LogSearchResult Unavailable(Uri? link) => new(available: false, events: [], link);
}

/// <summary>One reference code traced end to end: the API's facts merged with the log events of the same request.</summary>
public sealed record ReferenceCodeTrace
{
    /// <summary>The normalized reference code (<c>9C1E5-B7A20</c>).</summary>
    public required string ReferenceCode { get; init; }

    /// <summary>The request's correlation id, when known.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>The request's trace id (the correlation id when nothing names another), when known.</summary>
    public string? TraceId { get; init; }

    /// <summary>The earliest time the code was seen, when known.</summary>
    public DateTimeOffset? FirstSeen { get; init; }

    /// <summary>The request, when known.</summary>
    public TracedRequest? Request { get; init; }

    /// <summary>The request's log events, oldest first.</summary>
    public required IReadOnlyList<LoggedEvent> Events { get; init; }

    /// <summary>The log store answered (false: <see cref="Events"/> is empty because it could not be asked).</summary>
    public required bool LogsAvailable { get; init; }

    /// <summary>A link to the same search in the log store's UI, when it is configured.</summary>
    public Uri? LogsLink { get; init; }
}

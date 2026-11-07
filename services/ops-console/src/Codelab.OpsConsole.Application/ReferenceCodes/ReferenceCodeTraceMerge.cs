namespace Codelab.OpsConsole.Application.ReferenceCodes;

/// <summary>
/// Merges the API's facts and the log events into one trace. The API wins for every fact it states; the log events
/// fill the rest. A reference code is 40 bits of the correlation id, so two requests can share one: the trace keeps
/// only the events of the request it settled on (the API's correlation id, otherwise the earliest event's).
/// </summary>
internal static class ReferenceCodeTraceMerge
{
    internal static ReferenceCodeTrace Merge(string referenceCode, ApiReferenceCodeFacts? facts, LogSearchResult logs)
    {
        var known = facts ?? new ApiReferenceCodeFacts();
        var ordered = logs.Events.OrderBy(logged => logged.Timestamp).ToList();
        var correlationId = known.CorrelationId ?? FirstCorrelationId(ordered);
        var events = ordered.Where(logged => BelongsTo(logged, correlationId)).ToList();
        return new ReferenceCodeTrace
        {
            ReferenceCode = referenceCode,
            CorrelationId = correlationId,
            TraceId = known.TraceId ?? FirstTraceId(events) ?? correlationId,
            FirstSeen = known.FirstSeen ?? FirstTimestamp(events),
            Request = known.Request ?? FirstRequest(events),
            Events = events,
            LogsAvailable = logs.Available,
            LogsLink = logs.Link,
        };
    }

    private static string? FirstCorrelationId(List<LoggedEvent> events) =>
        events.Find(logged => logged.CorrelationId is not null)?.CorrelationId;

    private static string? FirstTraceId(List<LoggedEvent> events) =>
        events.Find(logged => logged.TraceId is not null)?.TraceId;

    private static DateTimeOffset? FirstTimestamp(List<LoggedEvent> events) =>
        events.Count == 0 ? null : events[0].Timestamp;

    private static TracedRequest? FirstRequest(List<LoggedEvent> events) =>
        events.Find(logged => logged.Request is not null)?.Request;

    private static bool BelongsTo(LoggedEvent logged, string? correlationId) =>
        logged.CorrelationId is null
        || correlationId is null
        || string.Equals(logged.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase);
}

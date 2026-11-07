using Codelab.OpsConsole.Application.ReferenceCodes;

namespace Codelab.OpsConsole.Api.Endpoints;

/// <summary>GET /ops-api/me: the operator's account name and the operator group that admitted them.</summary>
public sealed record MeResponse(string Name, IReadOnlyList<string> Groups);

/// <summary>One log event as the ops web shows it.</summary>
public sealed record TraceEventResponse(DateTimeOffset Timestamp, string Level, string Message);

/// <summary>The request a reference code belongs to.</summary>
public sealed record TraceRequestResponse(string Method, string Path, int Status);

/// <summary>
/// GET /ops-api/reference-codes/{code}: the request behind a reference code. <see cref="SeqAvailable"/> false means
/// Seq could not be asked, so <see cref="Events"/> is empty for that reason and not because there were none.
/// </summary>
public sealed record ReferenceCodeResponse
{
    /// <summary>The normalized reference code.</summary>
    public required string ReferenceCode { get; init; }

    /// <summary>The request's correlation id, when known.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>The request's trace id, when known.</summary>
    public string? TraceId { get; init; }

    /// <summary>When the code was first seen, when known.</summary>
    public DateTimeOffset? FirstSeen { get; init; }

    /// <summary>The request, when known.</summary>
    public TraceRequestResponse? Request { get; init; }

    /// <summary>The request's log events, oldest first.</summary>
    public required IReadOnlyList<TraceEventResponse> Events { get; init; }

    /// <summary>The same search in the Seq UI, when Seq is configured.</summary>
    public string? SeqUrl { get; init; }

    /// <summary>Seq answered.</summary>
    public required bool SeqAvailable { get; init; }

    /// <summary>The response for <paramref name="trace"/>.</summary>
    public static ReferenceCodeResponse From(ReferenceCodeTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        return new ReferenceCodeResponse
        {
            ReferenceCode = trace.ReferenceCode,
            CorrelationId = trace.CorrelationId,
            TraceId = trace.TraceId,
            FirstSeen = trace.FirstSeen,
            Request = trace.Request is { } request ? new TraceRequestResponse(request.Method, request.Path, request.Status) : null,
            Events = [.. trace.Events.Select(logged => new TraceEventResponse(logged.Timestamp, logged.Level, logged.Message))],
            SeqUrl = trace.LogsLink?.AbsoluteUri,
            SeqAvailable = trace.LogsAvailable,
        };
    }
}

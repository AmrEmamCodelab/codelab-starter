using Starter.Application.Results;
using Starter.Modules.Ops.Domain;

namespace Starter.Modules.Ops.Application.ReferenceCodes;

/// <summary>Where to find the request behind a reference code.</summary>
/// <param name="ReferenceCode">The code, uppercase.</param>
/// <param name="CorrelationId">The request's full correlation id (every log line of the request carries it).</param>
/// <param name="TraceId">The W3C trace id (the correlation id when it is 32 hex characters), or null.</param>
/// <param name="FirstSeen">When the error response completed.</param>
/// <param name="Method">The request's HTTP method.</param>
/// <param name="Path">The request path (no query string).</param>
/// <param name="StatusCode">The response status code.</param>
/// <param name="SearchFrom">The start of the log search window.</param>
/// <param name="SearchTo">The end of the log search window.</param>
public sealed record ReferenceCodeTrace(
    string ReferenceCode,
    string CorrelationId,
    string? TraceId,
    DateTimeOffset FirstSeen,
    string Method,
    string Path,
    int StatusCode,
    DateTimeOffset SearchFrom,
    DateTimeOffset SearchTo);

/// <summary>
/// <c>GET api/ops/reference-codes/{code}</c>: validates the code's shape (400 <see cref="OpsErrorCodes.ReferenceCodeInvalid"/>),
/// then answers the newest error response that showed it, with the window to search the log in
/// (<see cref="OpsSettings.SearchMargin"/> either side), or 404 <see cref="OpsErrorCodes.ReferenceCodeNotFound"/>.
/// </summary>
public sealed class FindReferenceCode(IReferenceCodeSightings sightings, OpsSettings settings)
{
    /// <summary>The length of a W3C trace id in hex.</summary>
    public const int TraceIdLength = 32;

    /// <summary>Looks <paramref name="code"/> up.</summary>
    public async Task<Outcome<ReferenceCodeTrace>> ExecuteAsync(string? code, CancellationToken cancellationToken)
    {
        var normalized = ReferenceCodeFormat.Normalize(code);
        if (normalized is null)
        {
            return Refusal.Invalid(OpsErrorCodes.ReferenceCodeInvalid);
        }

        var sighting = await sightings.FindNewestAsync(normalized, cancellationToken);
        return sighting is null ? Refusal.NotFound(OpsErrorCodes.ReferenceCodeNotFound) : TraceOf(sighting);
    }

    private ReferenceCodeTrace TraceOf(ReferenceCodeSighting sighting) =>
        new(
            ReferenceCode: sighting.ReferenceCode,
            CorrelationId: sighting.CorrelationId,
            TraceId: IsTraceId(sighting.CorrelationId) ? sighting.CorrelationId : null,
            FirstSeen: sighting.OccurredAt,
            Method: sighting.Method,
            Path: sighting.Path,
            StatusCode: sighting.StatusCode,
            SearchFrom: sighting.OccurredAt - settings.SearchMargin,
            SearchTo: sighting.OccurredAt + settings.SearchMargin);

    private static bool IsTraceId(string correlationId) =>
        correlationId.Length == TraceIdLength && correlationId.All(char.IsAsciiHexDigit);
}

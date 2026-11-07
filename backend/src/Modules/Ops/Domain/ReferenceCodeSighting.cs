using Starter.Domain.Auditing;

namespace Starter.Modules.Ops.Domain;

/// <summary>
/// One error response that showed a reference code to its caller (<c>ops.ReferenceCodeSightings</c>): the code, the
/// full correlation id (also the W3C trace id), the request line without its query string, the status and when.
/// Operators look a code up here to learn which request it was and where to search the log. Rows older than the
/// retention are purged by the <c>ops-reference-code-purge</c> job. Identifiers only, never a body or a user.
/// </summary>
[NotAudited]
public sealed class ReferenceCodeSighting
{
    private ReferenceCodeSighting()
    {
    }

    /// <summary>The internal id (never on the wire).</summary>
    public long Id { get; private set; }

    /// <summary>The reference code, uppercase (<see cref="ReferenceCodeFormat"/>).</summary>
    public string ReferenceCode { get; private set; } = string.Empty;

    /// <summary>The request's correlation id.</summary>
    public string CorrelationId { get; private set; } = string.Empty;

    /// <summary>The request's HTTP method.</summary>
    public string Method { get; private set; } = string.Empty;

    /// <summary>The request path, without the query string, cut to <see cref="OpsLimits.PathMaxLength"/>.</summary>
    public string Path { get; private set; } = string.Empty;

    /// <summary>The response status code.</summary>
    public int StatusCode { get; private set; }

    /// <summary>When the response completed.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>A new sighting, or null when <paramref name="details"/> carries no well-formed code or correlation id.</summary>
    public static ReferenceCodeSighting? From(ReferenceCodeSightingDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var code = ReferenceCodeFormat.Normalize(details.ReferenceCode);
        if (code is null || string.IsNullOrWhiteSpace(details.CorrelationId) || details.CorrelationId.Length > OpsLimits.CorrelationIdMaxLength)
        {
            return null;
        }

        return new ReferenceCodeSighting
        {
            ReferenceCode = code,
            CorrelationId = details.CorrelationId,
            Method = Cut(details.Method, OpsLimits.MethodMaxLength),
            Path = Cut(details.Path, OpsLimits.PathMaxLength),
            StatusCode = details.StatusCode,
            OccurredAt = details.OccurredAt,
        };
    }

    private static string Cut(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}

/// <summary>What a <see cref="ReferenceCodeSighting"/> records.</summary>
/// <param name="ReferenceCode">The reference code shown to the caller.</param>
/// <param name="CorrelationId">The request's correlation id.</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The request path without the query string.</param>
/// <param name="StatusCode">The response status code.</param>
/// <param name="OccurredAt">When the response completed.</param>
public sealed record ReferenceCodeSightingDetails(
    string ReferenceCode,
    string CorrelationId,
    string Method,
    string Path,
    int StatusCode,
    DateTimeOffset OccurredAt);

using System.Net;

namespace Codelab.Integration.Classification;

/// <summary>The classified failure of an outbound call.</summary>
/// <param name="Kind">What went wrong.</param>
/// <param name="StatusCode">The upstream status code, when the upstream answered.</param>
/// <param name="IsTransient">True when the same request may succeed if repeated (and is retry-safe).</param>
public sealed record OutboundFailure(OutboundFailureKind Kind, HttpStatusCode? StatusCode, bool IsTransient);

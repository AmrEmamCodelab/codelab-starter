using Codelab.Integration.Outbound;

namespace Codelab.Integration.Resilience;

/// <summary>
/// Decides whether a request may be sent twice. Fails closed: an unknown request is not retried, because a wrong "no"
/// costs one visible failure while a wrong "yes" can execute a payment or a submission twice.
/// </summary>
internal static class RetrySafety
{
    private static readonly HashSet<HttpMethod> SafeMethods = [HttpMethod.Get, HttpMethod.Head, HttpMethod.Options];

    public static bool IsRetrySafe(HttpRequestMessage? request) =>
        request is not null && (SafeMethods.Contains(request.Method) || request.IsMarkedIdempotent());
}

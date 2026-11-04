using Microsoft.AspNetCore.Http;

namespace Codelab.Observability;

/// <summary>
/// Adds the current correlation id (<see cref="CorrelationId.Current"/>) as <see cref="CorrelationId.Header"/> to a
/// request bound for a Codelab-owned service, so its log lines can be found under the caller's reference code. Added
/// only by <see cref="OutboundPropagation.AsInternalService"/>; an external client never sends it.
/// </summary>
public sealed class CorrelationIdForwardingHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (CorrelationId.Current(httpContextAccessor.HttpContext) is { } correlationId)
        {
            request.Headers.Remove(CorrelationId.Header);
            request.Headers.TryAddWithoutValidation(CorrelationId.Header, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}

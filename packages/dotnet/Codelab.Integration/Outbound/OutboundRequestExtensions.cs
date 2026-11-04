namespace Codelab.Integration.Outbound;

/// <summary>Per-request switches of the outbound engine.</summary>
public static class OutboundRequestExtensions
{
    private static readonly HttpRequestOptionsKey<bool> IdempotentKey = new("Codelab.Integration.Idempotent");

    /// <summary>
    /// Marks a request as safe to repeat, so the retry policy may retry it after a transient failure. Use it for a
    /// POST, PUT, PATCH or DELETE only when the upstream de-duplicates it (for example by an idempotency key the
    /// caller also sends). GET, HEAD and OPTIONS need no mark.
    /// </summary>
    public static HttpRequestMessage MarkIdempotent(this HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Options.Set(IdempotentKey, value: true);
        return request;
    }

    /// <summary>True when <see cref="MarkIdempotent"/> was called on the request.</summary>
    public static bool IsMarkedIdempotent(this HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Options.TryGetValue(IdempotentKey, out var idempotent) && idempotent;
    }
}

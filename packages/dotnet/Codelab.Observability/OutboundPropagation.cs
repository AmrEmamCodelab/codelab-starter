using Microsoft.Extensions.DependencyInjection;

namespace Codelab.Observability;

/// <summary>
/// Declares, per registered <c>HttpClient</c>, whether the correlation and trace context may leave this host on it.
/// Every <c>AddHttpClient</c> in a Codelab host ends in exactly one of these two calls; a client that ends in neither
/// still sends nothing, because <see cref="PropagationScope.DenyOutboundByDefault"/> is the process default.
/// <para>
/// <b>Certificate validation is never touched here.</b> The harvested internal-service handler accepted every server
/// certificate; this one keeps the platform's validation ON. Pinning or a private CA belongs to Codelab.Integration's
/// outbound policy, configured on the same <see cref="SocketsHttpHandler"/> after this call.
/// </para>
/// </summary>
public static class OutboundPropagation
{
    /// <summary>
    /// A Codelab-owned service: the primary handler becomes a <see cref="SocketsHttpHandler"/> carrying
    /// <see cref="PropagationScope.InternalPropagator"/> (trace context, no baggage), and every request carries the
    /// current <see cref="CorrelationId.Header"/>. Replaces any primary handler configured before it.
    /// </summary>
    public static IHttpClientBuilder AsInternalService(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<CorrelationIdForwardingHandler>();
        return builder
            .ConfigurePrimaryHttpMessageHandler(CreateInternalServiceHandler)
            .AddHttpMessageHandler<CorrelationIdForwardingHandler>();
    }

    /// <summary>
    /// Anything else (an identity provider, an e-mail or SMS provider): no <c>traceparent</c>, <c>tracestate</c>,
    /// <c>baggage</c> or correlation header. The primary handler and its certificate validation are left as they are;
    /// call it after any <c>ConfigurePrimaryHttpMessageHandler</c>.
    /// </summary>
    public static IHttpClientBuilder AsExternalService(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.ConfigurePrimaryHttpMessageHandler(static (handler, _) =>
        {
            if (handler is SocketsHttpHandler sockets)
            {
                sockets.ActivityHeadersPropagator = PropagationScope.ExternalPropagator;
            }
        });
    }

    /// <summary>The primary handler of an internal-service client: platform certificate validation, internal propagator.</summary>
    internal static SocketsHttpHandler CreateInternalServiceHandler() =>
        new() { ActivityHeadersPropagator = PropagationScope.InternalPropagator };
}

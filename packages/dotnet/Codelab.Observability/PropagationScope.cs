using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Codelab.Observability;

/// <summary>
/// How trace context may LEAVE a host: denied by default, opted into per client (<see cref="OutboundPropagation"/>).
/// The trace id is the user-visible correlation id, so a call to an external provider must not carry it. How it may
/// ENTER a host: never. An inbound <c>traceparent</c> would set the request's trace id, and so its correlation id and
/// reference code, letting any caller plant log lines and reference-code sightings under a code of its choosing.
/// </summary>
public static class PropagationScope
{
    /// <summary>The W3C trace-context header.</summary>
    public const string TraceParentHeader = "traceparent";

    /// <summary>The W3C trace-state header.</summary>
    public const string TraceStateHeader = "tracestate";

    /// <summary>Sends <c>traceparent</c>/<c>tracestate</c> only; never baggage, whose content a host does not control.</summary>
    public static DistributedContextPropagator InternalPropagator { get; } =
        new TraceContextOnlyPropagator(DistributedContextPropagator.CreateDefaultPropagator());

    /// <summary>Sends nothing.</summary>
    public static DistributedContextPropagator ExternalPropagator { get; } = DistributedContextPropagator.CreateNoOutputPropagator();

    /// <summary>
    /// Reads nothing and sends nothing: the propagator ASP.NET Core hosting resolves from DI to start each request's
    /// Activity. Registered by <c>AddCodelabObservability</c>, so every request starts a fresh, server-chosen trace
    /// whatever <c>traceparent</c>, <c>tracestate</c> or <c>baggage</c> the caller sent.
    /// </summary>
    public static DistributedContextPropagator InboundPropagator { get; } = new IgnoreEverythingPropagator();

    /// <summary>
    /// Closes both outbound paths, process-wide: the runtime default becomes <see cref="ExternalPropagator"/>, and the
    /// OpenTelemetry default becomes plain trace context (no baggage). Called by <c>AddCodelabObservability</c>, before
    /// any handler exists; a handler created earlier keeps the old default.
    /// </summary>
    public static void DenyOutboundByDefault()
    {
        DistributedContextPropagator.Current = ExternalPropagator;
        Sdk.SetDefaultTextMapPropagator(new TraceContextPropagator());
    }

    /// <summary>Wraps the runtime's propagator and passes only the W3C trace-context fields through.</summary>
    private sealed class TraceContextOnlyPropagator(DistributedContextPropagator inner) : DistributedContextPropagator
    {
        private static readonly HashSet<string> TraceContextFields =
            new(StringComparer.OrdinalIgnoreCase) { TraceParentHeader, TraceStateHeader };

        public override IReadOnlyCollection<string> Fields => TraceContextFields;

        public override void Inject(Activity? activity, object? carrier, PropagatorSetterCallback? setter)
        {
            if (setter is null)
            {
                return;
            }

            inner.Inject(activity, carrier, (c, name, value) =>
            {
                if (TraceContextFields.Contains(name))
                {
                    setter(c, name, value);
                }
            });
        }

        public override void ExtractTraceIdAndState(
            object? carrier, PropagatorGetterCallback? getter, out string? traceId, out string? traceState) =>
            inner.ExtractTraceIdAndState(carrier, getter, out traceId, out traceState);

        public override IEnumerable<KeyValuePair<string, string?>>? ExtractBaggage(object? carrier, PropagatorGetterCallback? getter) => null;
    }

    /// <summary>Extracts no trace context or baggage and injects nothing.</summary>
    private sealed class IgnoreEverythingPropagator : DistributedContextPropagator
    {
        public override IReadOnlyCollection<string> Fields { get; } = [];

        public override void Inject(Activity? activity, object? carrier, PropagatorSetterCallback? setter)
        {
        }

        public override void ExtractTraceIdAndState(
            object? carrier, PropagatorGetterCallback? getter, out string? traceId, out string? traceState)
        {
            traceId = null;
            traceState = null;
        }

        public override IEnumerable<KeyValuePair<string, string?>>? ExtractBaggage(object? carrier, PropagatorGetterCallback? getter) => null;
    }
}

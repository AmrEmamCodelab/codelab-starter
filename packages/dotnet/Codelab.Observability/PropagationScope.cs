using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Codelab.Observability;

/// <summary>
/// How trace context may LEAVE a host: denied by default, opted into per client (<see cref="OutboundPropagation"/>).
/// The trace id is the user-visible correlation id, so a call to an external provider must not carry it.
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
}

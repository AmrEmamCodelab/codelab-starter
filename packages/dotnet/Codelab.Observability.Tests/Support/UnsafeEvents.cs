using Serilog.Events;

namespace Codelab.Observability.Tests.Support;

/// <summary>The test hosts' PII predicate: an event carrying <see cref="MarkerProperty"/> must never reach a retained sink.</summary>
public static class UnsafeEvents
{
    public const string MarkerProperty = "UnsafeForRetainedSinks";

    public static bool IsUnsafe(LogEvent logEvent) =>
        RetainedSinkPolicy.IsUnsafeByDefault(logEvent) || logEvent.Properties.ContainsKey(MarkerProperty);
}

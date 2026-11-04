using Serilog.Core;
using Serilog.Events;

namespace Codelab.Observability;

/// <summary>
/// The default <see cref="ObservabilityOptions.IsUnsafeForRetainedSinks"/>: framework events that carry request or
/// data content a retained sink must not keep. A host that adds its own predicate combines it with
/// <see cref="IsUnsafeByDefault"/>.
/// <list type="bullet">
/// <item><see cref="HostingDiagnosticsSource"/>: "Request starting/finished" lines carry the full URL with its query
///   string, where a screen keeps what was typed into it. The Serilog request line (no query) replaces them.</item>
/// <item><see cref="DatabaseCommandSource"/>: EF Core's executed-command lines carry SQL text and, with sensitive
///   data logging, parameter values.</item>
/// </list>
/// </summary>
public static class RetainedSinkPolicy
{
    /// <summary>The ASP.NET Core hosting diagnostics category.</summary>
    public const string HostingDiagnosticsSource = "Microsoft.AspNetCore.Hosting.Diagnostics";

    /// <summary>The EF Core executed-command category.</summary>
    public const string DatabaseCommandSource = "Microsoft.EntityFrameworkCore.Database.Command";

    private static readonly HashSet<string> UnsafeSources = new(StringComparer.Ordinal) { HostingDiagnosticsSource, DatabaseCommandSource };

    /// <summary>True for an event from one of the unsafe framework sources.</summary>
    public static bool IsUnsafeByDefault(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        return logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var source)
            && source is ScalarValue { Value: string name }
            && UnsafeSources.Contains(name);
    }
}

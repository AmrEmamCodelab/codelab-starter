using System.Reflection;
using Serilog.Core;
using Serilog.Events;

namespace Codelab.Observability;

/// <summary>
/// What a host's CODE tells <see cref="ObservabilityBootstrap.AddCodelabObservability(Microsoft.Extensions.Hosting.IHostApplicationBuilder, ObservabilityOptions)"/>
/// about itself. Everything a deployment decides (file, Seq, tier, tracing, hash key) is configuration instead
/// (<see cref="ObservabilitySettings"/>).
/// </summary>
public sealed class ObservabilityOptions
{
    /// <summary>The host's service name: the <see cref="LogPropertyNames.Application"/> value and the log file prefix.</summary>
    public required string ServiceName { get; init; }

    /// <summary>The host's own assembly: its informational version is the <see cref="LogPropertyNames.Release"/> value.</summary>
    public required Assembly HostAssembly { get; init; }

    /// <summary>
    /// True for an event that must never reach a RETAINED sink (the file, Seq, any sink declared under
    /// <c>Serilog:WriteTo</c> or registered in DI), nor the console outside Development. Default:
    /// <see cref="RetainedSinkPolicy.IsUnsafeByDefault"/> (the framework request lines with the query string and the
    /// database command text); a host predicate should include it.
    /// </summary>
    public Func<LogEvent, bool> IsUnsafeForRetainedSinks { get; init; } = RetainedSinkPolicy.IsUnsafeByDefault;

    /// <summary>True for an event no sink may receive (a duplicate of a failure already logged once). Default: nothing.</summary>
    public Func<LogEvent, bool> IsExcludedFromEverySink { get; init; } = static _ => false;

    /// <summary>Host enrichers applied at the root, after the standard properties, so every sink agrees.</summary>
    public IReadOnlyList<ILogEventEnricher> Enrichers { get; init; } = [];

    /// <summary>
    /// Path prefixes (matched by segment, case-insensitively) whose request line is written at Verbose instead of
    /// Information and which create no server span. Default: the health endpoints.
    /// </summary>
    public IReadOnlyList<string> QuietRequestPaths { get; init; } = [HealthEndpoints.BasePath];

    /// <summary>
    /// Whether events also go to the <c>ILoggerProvider</c>s registered in DI. Off by default, because the framework's
    /// default providers would print every event, unfiltered, to stdout.
    /// </summary>
    public bool WriteToProviders { get; init; }

    /// <summary>
    /// Options for a host whose service name is <paramref name="serviceName"/> and whose code is the entry assembly.
    /// Under a test host the entry assembly is the test runner: prefer the <c>AddCodelabObservability(serviceName)</c>
    /// overload, or set <see cref="HostAssembly"/> to <c>typeof(Program).Assembly</c>.
    /// </summary>
    public static ObservabilityOptions For(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        return new ObservabilityOptions
        {
            ServiceName = serviceName,
            HostAssembly = Assembly.GetEntryAssembly() ?? typeof(ObservabilityOptions).Assembly,
        };
    }
}

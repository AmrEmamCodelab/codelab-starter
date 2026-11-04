using System.Collections.Concurrent;
using Serilog;
using Serilog.Configuration;

namespace Codelab.Observability.Tests.Support;

/// <summary>
/// A sink a test declares under <c>Serilog:WriteTo</c> (with <c>Serilog:Using</c> naming this assembly), so the
/// regression test can prove a CONFIGURED sink sits inside the retained sub-logger.
/// </summary>
public static class ConfiguredCaptureSink
{
    /// <summary>The name a test uses under <c>Serilog:WriteTo:N:Name</c>.</summary>
    public const string SinkName = nameof(CodelabTestCapture);

    private static readonly ConcurrentDictionary<string, CaptureSink> Sinks = new(StringComparer.Ordinal);

    /// <summary>The sink registered under <paramref name="captureName"/>, created on first use.</summary>
    public static CaptureSink For(string captureName) => Sinks.GetOrAdd(captureName, _ => new CaptureSink());

    /// <summary>The configuration method Serilog.Settings.Configuration binds to.</summary>
    public static LoggerConfiguration CodelabTestCapture(this LoggerSinkConfiguration sinkConfiguration, string captureName) =>
        sinkConfiguration.Sink(For(captureName));
}

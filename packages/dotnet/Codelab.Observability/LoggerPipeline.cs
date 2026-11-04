using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Settings.Configuration;

namespace Codelab.Observability;

/// <summary>The resolved inputs of one host's logger, computed once before the logger is built.</summary>
/// <param name="Options">What the host's code decided.</param>
/// <param name="Settings">What the deployment configured, already validated.</param>
/// <param name="Configuration">The host configuration (for the <c>Serilog</c> section).</param>
/// <param name="Environment">The host environment.</param>
internal sealed record LoggerPipelineInputs(
    ObservabilityOptions Options,
    ObservabilitySettings Settings,
    IConfiguration Configuration,
    IHostEnvironment Environment);

/// <summary>
/// Builds a host's Serilog pipeline. The root carries the levels, the standard properties, the PII destructuring
/// policy and the every-sink exclusion; one RETAINED sub-logger carries the file, Seq, the configured
/// (<c>Serilog:WriteTo</c>) and the DI-registered sinks, behind the host's PII predicate and the exception-text
/// redaction; the console gets the same predicate and a Warning floor outside Development.
/// </summary>
internal static class LoggerPipeline
{
    private const string LogFileNameSuffix = "-.json";

    /// <summary>Configures <paramref name="logger"/> for <paramref name="inputs"/>.</summary>
    public static void Configure(LoggerConfiguration logger, LoggerPipelineInputs inputs, IServiceProvider services)
    {
        ConfigureRoot(logger, inputs);
        ConfigureConsole(logger, inputs);
        logger.WriteTo.Logger(retained => ConfigureRetained(retained, inputs, services));
    }

    private static void ConfigureRoot(LoggerConfiguration logger, LoggerPipelineInputs inputs)
    {
        var options = inputs.Options;
        logger
            .ReadFrom.Configuration(SerilogConfigurationSlices.RootSlice(inputs.Configuration), ReaderOptions())
            .Enrich.FromLogContext()
            .Enrich.WithProperty(LogPropertyNames.Application, options.ServiceName)
            .Enrich.WithProperty(LogPropertyNames.Environment, inputs.Environment.EnvironmentName)
            .Enrich.WithProperty(
                LogPropertyNames.DeploymentEnvironment,
                DeploymentEnvironments.Resolve(inputs.Settings.DeploymentEnvironment, inputs.Environment))
            .Enrich.WithProperty(LogPropertyNames.Release, ReleaseVersion.Of(options.HostAssembly))
            .Enrich.WithProperty(LogPropertyNames.MachineName, System.Environment.MachineName)
            .Enrich.With([.. options.Enrichers])
            .Destructure.With(new PiiMaskingDestructuringPolicy())
            .Filter.ByExcluding(options.IsExcludedFromEverySink);
    }

    /// <summary>
    /// Development keeps the raw console at the configured levels. Elsewhere the console is retained (IIS writes it to
    /// the ANCM stdout file), so it gets the retained sinks' predicate, the redaction and a Warning floor.
    /// </summary>
    private static void ConfigureConsole(LoggerConfiguration logger, LoggerPipelineInputs inputs)
    {
        if (inputs.Environment.IsDevelopment())
        {
            logger.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
            return;
        }

        logger.WriteTo.Logger(console => console
            .Filter.ByExcluding(inputs.Options.IsUnsafeForRetainedSinks)
            .WriteTo.Sink(
                LoggerSinkConfiguration.Wrap(ExceptionTextRedaction.Wrap, wrapped => wrapped.Console(formatProvider: CultureInfo.InvariantCulture)),
                restrictedToMinimumLevel: LogEventLevel.Warning));
    }

    /// <summary>
    /// The retained sub-logger. Verbose, so it passes whatever the root admitted; filtered, NOT level-overridden,
    /// because Serilog evaluates source overrides on the root only.
    /// </summary>
    private static void ConfigureRetained(LoggerConfiguration retained, LoggerPipelineInputs inputs, IServiceProvider services)
    {
        retained
            .MinimumLevel.Verbose()
            .Filter.ByExcluding(inputs.Options.IsUnsafeForRetainedSinks)
            .WriteTo.Sink(LoggerSinkConfiguration.Wrap(
                ExceptionTextRedaction.Wrap,
                sinks => WriteRetainedSinks(sinks, inputs, services)));
    }

    private static void WriteRetainedSinks(LoggerSinkConfiguration sinks, LoggerPipelineInputs inputs, IServiceProvider services)
    {
        var settings = inputs.Settings;
        if (settings.LogFile.Enabled)
        {
            sinks.File(
                formatter: new CompactJsonFormatter(),
                path: Path.Combine(settings.LogFile.ResolvedDirectory, LogFileName(inputs.Options.ServiceName)),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: settings.LogFile.FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: settings.LogFile.RetainedFileCountLimit,
                flushToDiskInterval: TimeSpan.FromSeconds(1));
        }

        if (settings.Seq.Enabled)
        {
            sinks.Seq(
                settings.Seq.ServerUrl!.Trim(),
                apiKey: settings.Seq.ApiKey,
                queueSizeLimit: settings.Seq.QueueSizeLimit,
                formatProvider: CultureInfo.InvariantCulture);
        }

        sinks.Logger(declared => declared
            .MinimumLevel.Verbose()
            .ReadFrom.Configuration(SerilogConfigurationSlices.SinkSlice(inputs.Configuration), ReaderOptions())
            .ReadFrom.Services(services));
    }

    internal static string LogFileName(string serviceName) => serviceName + LogFileNameSuffix;

    private static ConfigurationReaderOptions ReaderOptions() => new() { SectionName = SerilogConfigurationSlices.SectionName };
}

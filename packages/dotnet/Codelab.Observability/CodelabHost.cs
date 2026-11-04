using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Formatting.Compact;

namespace Codelab.Observability;

/// <summary>
/// A host's <c>Main</c>: <c>return CodelabHost.Run(args, BuildAsync, options);</c>. It builds and runs the host,
/// and when either fails it leaves ONE <c>Fatal</c> in a node-local startup file
/// (<c>{logs}/{ServiceName}-startup-.json</c>) and returns <see cref="StartupFailureExitCode"/>, so "the API does
/// not start" has an answer on the node even when the host's own logger was never built.
/// <para>
/// Only when the process belongs to the host (its assembly is the entry assembly). Under a test host a failure
/// propagates unchanged to the test that expects it.
/// </para>
/// </summary>
public static class CodelabHost
{
    /// <summary>The exit code of a host that failed to build or start.</summary>
    public const int StartupFailureExitCode = 1;

    /// <summary>The exit code of a host that ran and shut down.</summary>
    public const int SuccessExitCode = 0;

    private const string StartupFileNameSuffix = "-startup-.json";
    private const string StartupFailureTemplate = "{Application} failed to start: {ExceptionType}.";

    /// <summary>Builds the host with <paramref name="build"/> and runs it until shutdown.</summary>
    public static int Run<THost>(string[] args, Func<string[], Task<THost>> build, ObservabilityOptions options)
        where THost : IHost
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(options);

        if (Assembly.GetEntryAssembly() != options.HostAssembly)
        {
            RunHost(args, build);
            return SuccessExitCode;
        }

        return RunOwningProcess(args, build, options);
    }

    /// <summary>The owning-process path: logs a startup failure once and returns <see cref="StartupFailureExitCode"/>.</summary>
    internal static int RunOwningProcess<THost>(string[] args, Func<string[], Task<THost>> build, ObservabilityOptions options)
        where THost : IHost
    {
        try
        {
            RunHost(args, build);
            return SuccessExitCode;
        }
        catch (Exception exception) when (exception is not (HostAbortedException or OperationCanceledException))
        {
            // Logged once, here, and not rethrown: this is the top of the process and the exit code is the signal.
            LogStartupFailure(args, options, exception);
            return StartupFailureExitCode;
        }
    }

    private static void RunHost<THost>(string[] args, Func<string[], Task<THost>> build)
        where THost : IHost
    {
        var host = build(args).GetAwaiter().GetResult();
        host.Run();
    }

    /// <summary>
    /// One <c>Fatal</c> with the exception (message and stack, redacted) and the fingerprint: it is the only record
    /// of why the host would not start, and startup validators name the offending key for exactly this reader.
    /// </summary>
    private static void LogStartupFailure(string[] args, ObservabilityOptions options, Exception exception)
    {
        var settings = ObservabilitySettings.From(StartupConfiguration(args));
        var path = Path.Combine(settings.LogFile.ResolvedDirectory, options.ServiceName + StartupFileNameSuffix);
        var fingerprint = FailureFingerprint.For(
            new FingerprintInput(exception.GetType(), FailureFingerprint.StartupSite(options.ServiceName)));

        using var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.WithProperty(LogPropertyNames.Application, options.ServiceName)
            .Enrich.WithProperty(LogPropertyNames.Release, ReleaseVersion.Of(options.HostAssembly))
            .Enrich.WithProperty(LogPropertyNames.MachineName, Environment.MachineName)
            .Enrich.WithProperty(LogPropertyNames.ErrorFingerprint, fingerprint)
            .WriteTo.Sink(Serilog.Configuration.LoggerSinkConfiguration.Wrap(
                ExceptionTextRedaction.Wrap,
                file => file.File(new CompactJsonFormatter(), path, rollingInterval: RollingInterval.Day)))
            .CreateLogger();
        logger.Fatal(exception, StartupFailureTemplate, options.ServiceName, exception.GetType().FullName);
    }

    /// <summary>
    /// The configuration the host would have seen, rebuilt without the host: appsettings, environment variables and
    /// the command line. A settings file the host could not parse (that may be the failure) is skipped.
    /// </summary>
    internal static IConfiguration StartupConfiguration(string[] args)
    {
        try
        {
            return new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile(StartupSettingsFile, optional: true)
                .AddEnvironmentVariables()
                .AddCommandLine(args)
                .Build();
        }
        catch (InvalidDataException)
        {
            return new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args).Build();
        }
    }

    private const string StartupSettingsFile = "appsettings.json";
}

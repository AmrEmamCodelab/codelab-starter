using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Codelab.Observability;

/// <summary>
/// The one observability bootstrap every Codelab host shares, instead of a copy of this policy per host. It reads
/// <see cref="ObservabilitySettings"/> (section <c>Observability</c>) for the file, Seq, tier, tracing and user-hash
/// key, and the <c>Serilog</c> section for levels and any extra sinks (<see cref="SerilogConfigurationSlices"/>).
/// </summary>
public static class ObservabilityBootstrap
{
    /// <summary>
    /// Registers observability for a host named <paramref name="serviceName"/> whose code is the assembly named by the
    /// host's application name (<c>IHostEnvironment.ApplicationName</c>, the API's assembly also under a test host), so
    /// <see cref="LogPropertyNames.Release"/> is the API's version. See
    /// <see cref="AddCodelabObservability(IHostApplicationBuilder, ObservabilityOptions)"/>.
    /// </summary>
    public static IHostApplicationBuilder AddCodelabObservability(this IHostApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        var options = new ObservabilityOptions
        {
            ServiceName = serviceName,
            HostAssembly = HostAssemblies.FromApplicationName(builder.Environment.ApplicationName),
        };
        return builder.AddCodelabObservability(options);
    }

    /// <summary>
    /// Configures the host's Serilog logger, the opt-in tracer, the <see cref="UserIdHash"/>, the
    /// <see cref="JobFailureLogFilter"/>, health checks, the outbound propagation default and the inbound one (a caller's
    /// <c>traceparent</c> is ignored). Throws
    /// <see cref="InvalidOperationException"/>, naming the key, for a setting the host must refuse to start with.
    /// <para>
    /// Every event carries <see cref="LogPropertyNames.Application"/>, <see cref="LogPropertyNames.Environment"/>,
    /// <see cref="LogPropertyNames.DeploymentEnvironment"/>, <see cref="LogPropertyNames.Release"/> and
    /// <see cref="LogPropertyNames.MachineName"/>. The host OWNS its logger (<c>preserveStaticLogger</c>), so several
    /// hosts in one test process never write through each other's configuration.
    /// </para>
    /// </summary>
    public static IHostApplicationBuilder AddCodelabObservability(this IHostApplicationBuilder builder, ObservabilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        var environment = builder.Environment;
        var settings = ObservabilitySettings.From(builder.Configuration);
        settings.Validate(environment.IsDevelopment());
        _ = DeploymentEnvironments.Resolve(settings.DeploymentEnvironment, environment);
        var inputs = new LoggerPipelineInputs(options, settings, builder.Configuration, environment);

        PropagationScope.DenyOutboundByDefault();
        IgnoreInboundTraceContext(builder.Services);
        RegisterServices(builder.Services, settings, environment);
        builder.Services.Configure<QuietRequestPathsOptions>(quiet => quiet.Paths = options.QuietRequestPaths);

        builder.Services.AddSerilog(
            (services, logger) => LoggerPipeline.Configure(logger, inputs, services),
            preserveStaticLogger: true,
            writeToProviders: options.WriteToProviders);

        builder.AddCodelabTracing(options, settings.Tracing);
        return builder;
    }

    /// <summary>
    /// Replaces the propagator ASP.NET Core hosting reads the inbound <c>traceparent</c> with: a request's trace id is
    /// its correlation id, so no caller may choose it (<see cref="PropagationScope.InboundPropagator"/>).
    /// </summary>
    internal static void IgnoreInboundTraceContext(IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Singleton(PropagationScope.InboundPropagator));

    private static void RegisterServices(IServiceCollection services, ObservabilitySettings settings, IHostEnvironment environment)
    {
        services.AddSingleton(UserIdHash.Create(settings.UserIdHash, environment));
        services.AddHostedService<UserIdHashStartupNotice>();
        services.TryAddSingleton<IFailureCodeResolver, DefaultFailureCodeResolver>();
        services.TryAddSingleton<JobFailureLogFilter>();
        services.AddHealthChecks();
    }
}

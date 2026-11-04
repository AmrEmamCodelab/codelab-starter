using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Codelab.Observability;

/// <summary>
/// The opt-in OpenTelemetry tracer. With <c>Observability:Tracing:Enabled</c> false (the default) nothing is
/// registered and nothing is exported. When true, server spans (ASP.NET Core) and client spans (HttpClient) are
/// exported over OTLP to <c>Observability:Tracing:OtlpEndpoint</c>.
/// <para>
/// PII posture: exception events are not recorded on spans (the message can quote data; the log line keeps it,
/// redacted), the query string is never recorded, and the <see cref="ObservabilityOptions.QuietRequestPaths"/>
/// make no span at all. SQL client spans are not collected, because statement text can carry values.
/// </para>
/// </summary>
public static class TracingBootstrap
{
    /// <summary>Registers the tracer when <paramref name="settings"/> enables it; returns whether it did.</summary>
    public static bool AddCodelabTracing(
        this IHostApplicationBuilder builder, ObservabilityOptions options, TracingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled)
        {
            return false;
        }

        var endpoint = new Uri(settings.OtlpEndpoint!, UriKind.Absolute);
        var quietPaths = options.QuietRequestPaths.Select(p => new PathString(p)).ToArray();

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: options.ServiceName, serviceVersion: ReleaseVersion.Of(options.HostAssembly)))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(aspNetCore =>
                {
                    aspNetCore.RecordException = false;
                    aspNetCore.Filter = http => !IsQuiet(http.Request.Path, quietPaths);
                })
                .AddHttpClientInstrumentation(http => http.RecordException = false)
                .AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = endpoint;
                    otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
                }));
        return true;
    }

    internal static bool IsQuiet(PathString path, IReadOnlyList<PathString> quietPaths) =>
        quietPaths.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
}

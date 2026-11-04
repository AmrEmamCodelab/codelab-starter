using Codelab.Observability.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Serilog.Events;

namespace Codelab.Observability.Tests;

/// <summary>The one bootstrap: standard properties, configured levels at the root, configured sinks inside the retained policy.</summary>
public sealed class ObservabilityBootstrapTests
{
    [Fact]
    public async Task Every_event_carries_the_standard_properties()
    {
        var (app, sink) = await TestHosts.StartAsync(TestHosts.Pipeline(_ => { }));
        await using var _ = app;

        var probeLogger = app.Services.GetRequiredService<ILogger<ObservabilityBootstrapTests>>();
        TestLog.Probe(probeLogger);

        var probe = sink.Events.Single(e => e.MessageTemplate.Text == "Probe");
        Assert.Equal($"\"{TestHosts.ServiceName}\"", probe.Properties[LogPropertyNames.Application].ToString());
        Assert.Equal("\"production\"", probe.Properties[LogPropertyNames.DeploymentEnvironment].ToString());
        Assert.Equal("\"Production\"", probe.Properties[LogPropertyNames.Environment].ToString());
        Assert.True(probe.Properties.ContainsKey(LogPropertyNames.Release));
        Assert.True(probe.Properties.ContainsKey(LogPropertyNames.MachineName));
    }

    /// <summary>
    /// Regression for the root <c>ReadFrom.Configuration</c> defect: a sink declared under <c>Serilog:WriteTo</c> must sit
    /// INSIDE the retained policy (no unsafe event, redacted exception text), and the root must still honour the
    /// configured minimum level.
    /// </summary>
    [Fact]
    public async Task A_configured_sink_gets_the_retained_policy_and_the_root_honours_configured_levels()
    {
        var captureName = Guid.NewGuid().ToString("N");
        var spec = TestHosts.Pipeline(_ => { }) with { Settings = SerilogSettings(captureName, minimumLevel: "Warning") };
        var (app, _) = await TestHosts.StartAsync(spec);
        await using var _ = app;
        var logger = app.Services.GetRequiredService<ILogger<ObservabilityBootstrapTests>>();

        TestLog.BelowLevel(logger);
        using (logger.BeginScope(new Dictionary<string, object> { [UnsafeEvents.MarkerProperty] = true }))
        {
            TestLog.Unsafe(logger);
        }

        TestLog.SaveFailed(logger, new InvalidOperationException("The duplicate key value is (jane@example.test)."));

        var configured = ConfiguredCaptureSink.For(captureName);
        Assert.Equal(["Save failed"], configured.Events.Select(e => e.MessageTemplate.Text));
        Assert.DoesNotContain("jane@example.test", configured.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_level_override_from_configuration_applies_at_the_root()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Serilog:MinimumLevel:Default"] = "Debug",
            ["Serilog:MinimumLevel:Override:Noisy"] = "Error",
        };
        var (app, sink) = await TestHosts.StartAsync(TestHosts.Pipeline(_ => { }) with { Settings = settings });
        await using var _ = app;
        var factory = app.Services.GetRequiredService<ILoggerFactory>();

        var noisy = factory.CreateLogger("Noisy.Component");
        var quiet = factory.CreateLogger("Quiet.Component");
        TestLog.Suppressed(noisy);
        TestLog.Admitted(quiet);

        Assert.DoesNotContain(sink.Events, e => e.MessageTemplate.Text == "Suppressed by override");
        Assert.Contains(sink.Events, e => e.MessageTemplate.Text == "Admitted by default" && e.Level == LogEventLevel.Debug);
    }

    [Fact]
    public async Task Tracing_is_off_by_default_and_on_when_configured()
    {
        var (off, _) = await TestHosts.StartAsync(TestHosts.Pipeline(_ => { }));
        await using (off)
        {
            Assert.Null(off.Services.GetService<TracerProvider>());
        }

        var settings = new Dictionary<string, string?>
        {
            ["Observability:Tracing:Enabled"] = "true",
            ["Observability:Tracing:OtlpEndpoint"] = "http://127.0.0.1:9/v1/traces",
        };
        var (on, _) = await TestHosts.StartAsync(TestHosts.Pipeline(_ => { }) with { Settings = settings });
        await using (on)
        {
            Assert.NotNull(on.Services.GetService<TracerProvider>());
        }
    }

    [Fact]
    public void An_invalid_setting_refuses_to_build_the_host()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{SeqSettings.KeyPrefix}:{nameof(SeqSettings.ServerUrl)}"] = "http://seq.example.test",
        });

        Assert.Throws<InvalidOperationException>(() => builder.AddCodelabObservability(TestHosts.ServiceName));
    }

    [Fact]
    public async Task The_failure_code_resolver_and_job_filter_are_registered()
    {
        var (app, _) = await TestHosts.StartAsync(TestHosts.Pipeline(_ => { }));
        await using var _ = app;

        Assert.IsType<DefaultFailureCodeResolver>(app.Services.GetRequiredService<IFailureCodeResolver>());
        Assert.NotNull(app.Services.GetRequiredService<JobFailureLogFilter>());
        Assert.False(app.Services.GetRequiredService<UserIdHash>().IsEphemeral);
    }

    private static Dictionary<string, string?> SerilogSettings(string captureName, string minimumLevel) => new()
    {
        ["Serilog:MinimumLevel:Default"] = minimumLevel,
        ["Serilog:Using:0"] = typeof(ConfiguredCaptureSink).Assembly.GetName().Name,
        ["Serilog:WriteTo:0:Name"] = ConfiguredCaptureSink.SinkName,
        ["Serilog:WriteTo:0:Args:captureName"] = captureName,
    };
}

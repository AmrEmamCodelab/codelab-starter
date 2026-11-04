using System.Reflection;
using Codelab.Observability.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace Codelab.Observability.Tests;

/// <summary>The Release on every line is the host's version, not the test runner's (the entry assembly under a test).</summary>
public sealed class HostAssembliesTests
{
    private static readonly Assembly ThisAssembly = typeof(HostAssembliesTests).Assembly;

    [Fact]
    public void The_application_name_resolves_the_host_assembly_and_an_unknown_name_falls_back_to_the_entry_assembly()
    {
        var entry = Assembly.GetEntryAssembly() ?? typeof(HostAssemblies).Assembly;

        Assert.Same(ThisAssembly, HostAssemblies.FromApplicationName(ThisAssembly.GetName().Name));
        Assert.Same(entry, HostAssemblies.FromApplicationName("Not.An.Assembly.Anywhere"));
        Assert.Same(entry, HostAssemblies.FromApplicationName(applicationName: null));
    }

    [Fact]
    public async Task The_service_name_overload_logs_the_application_assemblys_release_not_the_test_runners()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ApplicationName = ThisAssembly.GetName().Name,
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(TestHosts.DefaultSettings());
        var sink = new CaptureSink();
        builder.Services.AddSingleton<ILogEventSink>(sink);
        builder.AddCodelabObservability(TestHosts.ServiceName);
        await using var app = builder.Build();
        await app.StartAsync();

        var logger = app.Services.GetRequiredService<ILogger<HostAssembliesTests>>();
        TestLog.Probe(logger);

        var probe = sink.Events.Single(logEvent => logEvent.MessageTemplate.Text == "Probe");
        var release = Assert.IsType<ScalarValue>(probe.Properties[LogPropertyNames.Release]).Value;
        Assert.Equal(ReleaseVersion.Of(ThisAssembly), release);
        Assert.NotEqual(ReleaseVersion.Of(Assembly.GetEntryAssembly()!), release);
    }
}

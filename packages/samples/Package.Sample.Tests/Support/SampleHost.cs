using Codelab.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog.Core;

namespace Package.Sample.Tests.Support;

/// <summary>
/// The sample API started in-process as a Production host (the published-material guard is on), configured with
/// <see cref="SampleSettings"/>. Extra log sinks (the PII canary) are registered in DI, where the observability
/// package puts them behind its retained-sink policy like any configured sink.
/// </summary>
internal sealed class SampleHost : WebApplicationFactory<Program>
{
    /// <summary>A host name outside the HSTS exclusions (localhost is excluded), so HSTS is observable.</summary>
    public static readonly Uri BaseAddress = new("https://sample.test");

    private readonly IReadOnlyDictionary<string, string?> _settings;
    private readonly IReadOnlyList<ILogEventSink> _sinks;

    public SampleHost(SampleKeys keys, Action<IDictionary<string, string?>>? adjust = null, params ILogEventSink[] sinks)
    {
        Keys = keys;
        var settings = SampleSettings.Production(keys);
        adjust?.Invoke(settings);
        _settings = settings;
        _sinks = sinks;
    }

    public SampleKeys Keys { get; }

    public SecurityProtocol Protocol => Services.GetRequiredService<SecurityProtocol>();

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = BaseAddress, AllowAutoRedirect = false });

    public SessionClient NewSession(string? userId = null) => new(this, userId ?? "user-" + Guid.NewGuid().ToString("N")[..8]);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        // UseSetting reaches the host as command-line configuration (highest precedence), which the packages read
        // while Program registers them; a configuration source added later would be seen too late.
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            foreach (var sink in _sinks)
            {
                services.AddSingleton(sink);
            }
        });
    }
}

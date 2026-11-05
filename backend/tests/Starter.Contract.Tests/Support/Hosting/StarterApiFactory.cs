using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Starter.Tests.Hosting;

/// <summary>
/// The real host (Program) in memory, with test settings. Each setting is applied twice on purpose: as a host setting,
/// so code that reads configuration while Program registers services sees it, and as the last configuration source,
/// so it also wins over a developer's local secrets file loaded by Program in Development.
/// </summary>
/// <param name="environmentName">The host environment (Development, Production, ...).</param>
/// <param name="settings">The configuration the host runs with.</param>
/// <param name="configureServices">Test service overrides, or null.</param>
public sealed class StarterApiFactory(
    string environmentName,
    IReadOnlyDictionary<string, string?> settings,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    /// <summary>The settings the host runs with.</summary>
    public IReadOnlyDictionary<string, string?> Settings { get; } = settings;

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment(environmentName);
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(Settings));
        if (configureServices is not null)
        {
            builder.ConfigureTestServices(configureServices);
        }
    }
}

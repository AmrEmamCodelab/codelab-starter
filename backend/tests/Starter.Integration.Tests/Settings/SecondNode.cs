using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Starter.Application.Settings;
using Starter.Infrastructure.Persistence;
using Starter.Integration.Tests.Support;
using Starter.Modules.Settings.Infrastructure.Options;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests.Settings;

/// <summary>
/// A second API node on the first host's database: the same settings and key rings (as two IIS nodes behind one load
/// balancer share them), its own DI container (so its own settings snapshot cache), a <see cref="FakeTimeProvider"/>
/// clock, and no migrations of its own. Disposing it stops the node; the database belongs to the first host.
/// </summary>
internal sealed class SecondNode : IAsyncDisposable
{
    private readonly StarterApiFactory _factory;

    private SecondNode(StarterApiFactory factory, FakeTimeProvider clock)
    {
        _factory = factory;
        Clock = clock;
    }

    /// <summary>The node's clock (starts at the real time; moves only when the test advances it).</summary>
    public FakeTimeProvider Clock { get; }

    /// <summary>Starts a node beside <paramref name="first"/>; <paramref name="checkStampPerScope"/> sets <c>Settings:CheckStampPerScope</c>.</summary>
    public static SecondNode Start(IntegrationHost first, bool checkStampPerScope)
    {
        ArgumentNullException.ThrowIfNull(first);
        var settings = new Dictionary<string, string?>(first.Factory.Settings, StringComparer.OrdinalIgnoreCase)
        {
            [StarterHostSettings.Key(DatabaseOptions.SectionName, nameof(DatabaseOptions.ApplyMigrationsOnStartup))] = bool.FalseString,
            [StarterHostSettings.Key(SettingsOptions.SectionName, nameof(SettingsOptions.CheckStampPerScope))] = checkStampPerScope.ToString(),
        };
        var clock = new FakeTimeProvider(TimeProvider.System.GetUtcNow());
        var factory = new StarterApiFactory(Environments.Development, settings, services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)));
        _ = factory.Services; // starts the node now, not on the first read
        return new SecondNode(factory, clock);
    }

    /// <summary>Reads an integer setting on this node, in a fresh DI scope (as one request would).</summary>
    public int ReadInt32(string key)
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IRuntimeSettings>().GetInt32(key);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}

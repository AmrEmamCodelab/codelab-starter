using Codelab.Security.Signing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Starter.Infrastructure.Modularity;
using Starter.Infrastructure.Persistence;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests.Support;

/// <summary>
/// The real host on its own throwaway database <c>StarterIt_&lt;guid&gt;</c>. Start: create the database, start the
/// host (which migrates every module DbContext, creates the security-store schema and seeds accounts through the
/// startup tasks). Stop: dispose the host, then drop ONLY that database, also when the start failed.
/// Request and response signing are Off: the tests drive the session flow, and signing is proven by the
/// Codelab.Security golden vectors and the browser check.
/// </summary>
public abstract class IntegrationHost : IAsyncLifetime, IDisposable
{
    private StarterApiFactory? _factory;

    /// <summary>Creates the fixture for <paramref name="environmentName"/>.</summary>
    protected IntegrationHost(string environmentName)
    {
        EnvironmentName = environmentName;
        DatabaseName = $"{ModuleNaming.SolutionTokenOf(typeof(Program).Assembly)}{TestSqlServer.DatabaseMarker}{Guid.NewGuid():N}";
    }

    /// <summary>The host environment.</summary>
    public string EnvironmentName { get; }

    /// <summary>The throwaway database this fixture owns.</summary>
    public string DatabaseName { get; }

    /// <summary>The accounts seeded at start.</summary>
    public StarterTestAccounts Accounts { get; } = StarterTestAccounts.Generate();

    /// <summary>The started host.</summary>
    public StarterApiFactory Factory => _factory ?? throw new InvalidOperationException("The host has not started.");

    /// <summary>A client of the started host.</summary>
    public StarterClient CreateClient() => new(Factory.CreateClient());

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await TestSqlServer.EnsureStartedAsync();
        await TestSqlServer.CreateDatabaseAsync(DatabaseName);
        try
        {
            _factory = new StarterApiFactory(EnvironmentName, StarterHostSettings.For(HostOptions(), Accounts), ConfigureServices);
            AssertUsesOwnDatabase(_factory.Services);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        try
        {
            if (_factory is not null)
            {
                await _factory.DisposeAsync();
                _factory = null;
            }
        }
        finally
        {
            await TestSqlServer.DropDatabaseAsync(DatabaseName);
        }
    }

    /// <summary>Disposes the host if <see cref="DisposeAsync"/> did not already (that path also drops the database).</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the host when <paramref name="disposing"/> is true.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _factory?.Dispose();
            _factory = null;
        }
    }

    /// <summary>True when the environment lets the kernel migrate on start (Development only).</summary>
    protected bool MigratesOnStart => string.Equals(EnvironmentName, Environments.Development, StringComparison.Ordinal);

    /// <summary>Test service overrides. Outside Development the kernel refuses to migrate, so the fixture migrates first.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
        if (!MigratesOnStart)
        {
            services.Insert(0, ServiceDescriptor.Singleton<IHostedService, MigrateBeforeStartupTasks>());
        }
    }

    private StarterHostOptions HostOptions() =>
        new(
            ConnectionString: TestSqlServer.ConnectionStringFor(DatabaseName),
            UsesDatabase: true,
            ApplyMigrationsOnStartup: MigratesOnStart,
            SigningMode: SecurityEnforcementMode.Off);

    private void AssertUsesOwnDatabase(IServiceProvider services)
    {
        var configured = services.GetRequiredService<IOptions<ConnectionStringsOptions>>().Value.Starter;
        if (configured is null || !configured.Contains(DatabaseName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The host is not using the test database {DatabaseName}.");
        }
    }

    /// <summary>Applies every module migration before the startup tasks run (outside Development).</summary>
    private sealed class MigrateBeforeStartupTasks(IServiceScopeFactory scopes) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var scope = scopes.CreateAsyncScope();
            foreach (var database in scope.ServiceProvider.GetServices<IModuleDatabase>())
            {
                await database.MigrateAsync(cancellationToken);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

/// <summary>The host in Development: migrations on start, the break-glass and the Development user seeded.</summary>
public sealed class DevelopmentHost() : IntegrationHost(Environments.Development);

/// <summary>The host in Production: only the break-glass account is seeded.</summary>
public sealed class ProductionHost() : IntegrationHost(Environments.Production);

/// <summary>Tests sharing the Development host.</summary>
[CollectionDefinition(Name)]
public sealed class DevelopmentHostGroup : ICollectionFixture<DevelopmentHost>
{
    /// <summary>The collection name.</summary>
    public const string Name = "development host";
}

/// <summary>Tests sharing the Production host.</summary>
[CollectionDefinition(Name)]
public sealed class ProductionHostGroup : ICollectionFixture<ProductionHost>
{
    /// <summary>The collection name.</summary>
    public const string Name = "production host";
}

using Codelab.Security.Signing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Starter.Infrastructure.Modularity;
using Starter.Infrastructure.Startup;
using Starter.Tests.Hosting;

namespace Starter.Contract.Tests.Support;

/// <summary>
/// The real host (Program, every module) without a database: Development (so the OpenAPI endpoint is mapped), no
/// migrations, no security-store schema, and no startup tasks (they seed through SQL). Requests that reach SQL are
/// out of scope here; the integration tests cover them on LocalDB. Signing stays as configured (Enforce).
/// </summary>
public sealed class ContractHost : IDisposable
{
    /// <summary>A connection string that is never opened (the host validates that one is configured).</summary>
    public const string UnusedConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=StarterContractNeverOpened;Integrated Security=true";

    /// <summary>Starts the host.</summary>
    public ContractHost()
    {
        var options = new StarterHostOptions(
            ConnectionString: UnusedConnectionString,
            UsesDatabase: false,
            ApplyMigrationsOnStartup: false,
            SigningMode: SecurityEnforcementMode.Enforce);
        Factory = new StarterApiFactory(Environments.Development, StarterHostSettings.For(options, StarterTestAccounts.Generate()), RemoveStartupTasks);
        Endpoints = [.. Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints];
        Modules = ModuleCatalog.Discover(typeof(Program).Assembly);
    }

    /// <summary>The host.</summary>
    public StarterApiFactory Factory { get; }

    /// <summary>Every endpoint of the host.</summary>
    public IReadOnlyList<Endpoint> Endpoints { get; }

    /// <summary>The modules the host runs.</summary>
    public ModuleCatalog Modules { get; }

    /// <summary>The endpoint owners present in this host: every module plus the kernel and the platform.</summary>
    public IReadOnlyCollection<string> PresentOwners =>
        [.. Modules.Modules.Select(module => module.Name), EndpointOwners.Kernel, EndpointOwners.Platform];

    /// <inheritdoc />
    public void Dispose() => Factory.Dispose();

    private static void RemoveStartupTasks(IServiceCollection services)
    {
        var runners = services.Where(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(StartupTaskRunner)).ToList();
        foreach (var runner in runners)
        {
            services.Remove(runner);
        }
    }
}

/// <summary>One contract host per test run.</summary>
[CollectionDefinition(Name)]
public sealed class ContractHostGroup : ICollectionFixture<ContractHost>
{
    /// <summary>The collection name.</summary>
    public const string Name = "contract host";
}

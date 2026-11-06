using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Infrastructure.Modularity;
using Starter.Modules.Lookups.Infrastructure;

namespace Starter.Modules.Lookups;

/// <summary>The Lookups module (ADR-004). The host finds it through its ProjectReference in Starter.Api.</summary>
public sealed class LookupsModule : IModule
{
    /// <inheritdoc />
    public string Name => ModuleNaming.NameOf(typeof(LookupsModule).Assembly);

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration) =>
        services.AddLookupsModule(configuration);

    /// <inheritdoc />
    public void UseModule(IEndpointRouteBuilder endpoints) => endpoints.UseLookupsModule();
}

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Infrastructure.Modularity;
using Starter.Modules.Ops.Infrastructure;

namespace Starter.Modules.Ops;

/// <summary>The Ops module (ADR-004). The host finds it through its ProjectReference in Starter.Api.</summary>
public sealed class OpsModule : IModule
{
    /// <inheritdoc />
    public string Name => ModuleNaming.NameOf(typeof(OpsModule).Assembly);

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration) =>
        services.AddOpsModule(configuration);

    /// <inheritdoc />
    public void UseModule(IEndpointRouteBuilder endpoints) => endpoints.UseOpsModule();
}

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Infrastructure.Modularity;
using Starter.Modules.Portal.Infrastructure;

namespace Starter.Modules.Portal;

/// <summary>The Portal module (module 15, ADR-004). The host finds it through its ProjectReference in Starter.Api.</summary>
public sealed class PortalModule : IModule
{
    /// <inheritdoc />
    public string Name => ModuleNaming.NameOf(typeof(PortalModule).Assembly);

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration) =>
        services.AddPortalModule(configuration);

    /// <inheritdoc />
    public void UseModule(IEndpointRouteBuilder endpoints) => endpoints.UsePortalModule();
}

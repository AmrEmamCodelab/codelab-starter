using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Infrastructure.Modularity;
using Starter.Modules.Access.Infrastructure;

namespace Starter.Modules.Access;

/// <summary>The Access module (ADR-004): the permission catalog, the permission handler and user grants.</summary>
public sealed class AccessModule : IModule
{
    /// <inheritdoc />
    public string Name => ModuleNaming.NameOf(typeof(AccessModule).Assembly);

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration) =>
        services.AddAccessModule(configuration);

    /// <inheritdoc />
    public void UseModule(IEndpointRouteBuilder endpoints) => endpoints.UseAccessModule();
}

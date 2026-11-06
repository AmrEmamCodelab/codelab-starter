using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Infrastructure.Modularity;
using Starter.Modules.ServiceRequests.Infrastructure;

namespace Starter.Modules.ServiceRequests;

/// <summary>The ServiceRequests module (ADR-004). The host finds it through its ProjectReference in Starter.Api.</summary>
public sealed class ServiceRequestsModule : IModule
{
    /// <inheritdoc />
    public string Name => ModuleNaming.NameOf(typeof(ServiceRequestsModule).Assembly);

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration) =>
        services.AddServiceRequestsModule(configuration);

    /// <inheritdoc />
    public void UseModule(IEndpointRouteBuilder endpoints) => endpoints.UseServiceRequestsModule();
}

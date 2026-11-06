using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Infrastructure.Modularity;
using Starter.Modules.Deadlines.Infrastructure;

namespace Starter.Modules.Deadlines;

/// <summary>Module 10, Deadlines and SLA timers (ADR-004). The host finds it through its ProjectReference in Starter.Api.</summary>
public sealed class DeadlinesModule : IModule
{
    /// <inheritdoc />
    public string Name => ModuleNaming.NameOf(typeof(DeadlinesModule).Assembly);

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration) =>
        services.AddDeadlinesModule(configuration);

    /// <inheritdoc />
    public void UseModule(IEndpointRouteBuilder endpoints) => endpoints.UseDeadlinesModule();
}

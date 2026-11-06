using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Infrastructure.Modularity;
using Starter.Modules.Assignments.Infrastructure;

namespace Starter.Modules.Assignments;

/// <summary>The Assignments module (ADR-004). The host finds it through its ProjectReference in Starter.Api.</summary>
public sealed class AssignmentsModule : IModule
{
    /// <inheritdoc />
    public string Name => ModuleNaming.NameOf(typeof(AssignmentsModule).Assembly);

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration) =>
        services.AddAssignmentsModule(configuration);

    /// <inheritdoc />
    public void UseModule(IEndpointRouteBuilder endpoints) => endpoints.UseAssignmentsModule();
}

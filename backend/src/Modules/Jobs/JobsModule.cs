using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Infrastructure.Modularity;
using Starter.Modules.Jobs.Infrastructure;

namespace Starter.Modules.Jobs;

/// <summary>The jobs module (ADR-004). The host finds it through its ProjectReference in Starter.Api.</summary>
public sealed class JobsModule : IModule
{
    /// <inheritdoc />
    public string Name => ModuleNaming.NameOf(typeof(JobsModule).Assembly);

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration) =>
        services.AddJobsModule(configuration);

    /// <inheritdoc />
    public void UseModule(IEndpointRouteBuilder endpoints) => endpoints.UseJobsModule();
}

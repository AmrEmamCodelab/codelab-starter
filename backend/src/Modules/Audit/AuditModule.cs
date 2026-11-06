using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Infrastructure.Modularity;
using Starter.Modules.Audit.Infrastructure;

namespace Starter.Modules.Audit;

/// <summary>The Audit module (ADR-004). The host finds it through its ProjectReference in Starter.Api.</summary>
public sealed class AuditModule : IModule
{
    /// <inheritdoc />
    public string Name => ModuleNaming.NameOf(typeof(AuditModule).Assembly);

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration) =>
        services.AddAuditModule(configuration);

    /// <inheritdoc />
    public void UseModule(IEndpointRouteBuilder endpoints) => endpoints.UseAuditModule();
}

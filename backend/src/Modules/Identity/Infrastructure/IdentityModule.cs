using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Infrastructure.Modularity;

namespace Starter.Modules.Identity.Infrastructure;

/// <summary>Module 1, Identity: login, the session issuer, logout and the seeded accounts (see MODULE.md).</summary>
public sealed class IdentityModule : IModule
{
    /// <inheritdoc />
    public string Name { get; } = ModuleNaming.NameOf(typeof(IdentityModule).Assembly);

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration) => services.AddIdentityModule(configuration);

    /// <inheritdoc />
    public void UseModule(IEndpointRouteBuilder endpoints) => endpoints.UseIdentityModule();
}

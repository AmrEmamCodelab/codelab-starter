using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Infrastructure.Modularity;
using Starter.Modules.Identity.Infrastructure.LoginModes;

namespace Starter.Modules.Identity.Infrastructure;

/// <summary>
/// Module 1, Identity: login (basic, Active Directory and SSO, switched by <c>Auth:Modes</c>), the session issuer,
/// logout, account administration and the seeded accounts (see MODULE.md).
/// </summary>
public sealed class IdentityModule : IModule
{
    /// <inheritdoc />
    public string Name { get; } = ModuleNaming.NameOf(typeof(IdentityModule).Assembly);

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration) =>
        services.AddIdentityModule(configuration).AddIdentityLoginModes(configuration);

    /// <inheritdoc />
    public void UseModule(IEndpointRouteBuilder endpoints) => endpoints.UseIdentityModule().UseIdentityLoginModes();
}

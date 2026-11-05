using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Infrastructure.Modularity;
using Starter.Modules.Users.Infrastructure;

namespace Starter.Modules.Users;

/// <summary>The Users module (ADR-004). The host finds it through its ProjectReference in Starter.Api.</summary>
public sealed class UsersModule : IModule
{
    /// <inheritdoc />
    public string Name => ModuleNaming.NameOf(typeof(UsersModule).Assembly);

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration) =>
        services.AddUsersModule(configuration);

    /// <inheritdoc />
    public void UseModule(IEndpointRouteBuilder endpoints) => endpoints.UseUsersModule();
}

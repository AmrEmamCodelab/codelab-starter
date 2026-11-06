using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Infrastructure.Modularity;
using Starter.Modules.Notifications.Infrastructure;

namespace Starter.Modules.Notifications;

/// <summary>The Notifications module (module 12, ADR-004). The host finds it through its ProjectReference in Starter.Api.</summary>
public sealed class NotificationsModule : IModule
{
    /// <inheritdoc />
    public string Name => ModuleNaming.NameOf(typeof(NotificationsModule).Assembly);

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration) =>
        services.AddNotificationsModule(configuration);

    /// <inheritdoc />
    public void UseModule(IEndpointRouteBuilder endpoints) => endpoints.UseNotificationsModule();
}

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Infrastructure.Modularity;
using Starter.Modules.Attachments.Infrastructure;

namespace Starter.Modules.Attachments;

/// <summary>
/// The Attachments module (module 13, ADR-004). The host finds it through its ProjectReference in Starter.Api; its
/// registrations are in <see cref="AttachmentsModuleRegistration"/>.
/// </summary>
public sealed class AttachmentsModule : IModule
{
    /// <inheritdoc />
    public string Name => ModuleNaming.NameOf(typeof(AttachmentsModule).Assembly);

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration) =>
        services.AddAttachmentsModule(configuration);

    /// <inheritdoc />
    public void UseModule(IEndpointRouteBuilder endpoints) => endpoints.UseAttachmentsModule();
}

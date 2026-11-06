using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Starter.Api.Hosting;
using Starter.Application.Authorization;
using Starter.Application.Workflow;
using Starter.Infrastructure.Modularity;

namespace Starter.DocsGen.Collection;

/// <summary>
/// Reads the surface from the real host: the served OpenAPI document, the permission catalog sources and the
/// endpoint data source from its services, the error codes and options classes by reflection over the solution and
/// package assemblies, the status enums by reflection over the solution's own assemblies, and the registered workflow
/// definitions (cross-checked against the definition classes those assemblies declare).
/// </summary>
internal sealed class HostSurfaceCollector : IApiSurfaceSource
{
    /// <summary>The path the host serves the document at.</summary>
    public const string OpenApiPath = "/openapi/" + StarterOpenApi.DocumentName + ".json";

    /// <inheritdoc />
    public async Task<ApiSurface> CollectAsync(CancellationToken cancellationToken)
    {
        var hostAssembly = typeof(Program).Assembly;
        var modules = ModuleCatalog.Discover(hostAssembly);
        var owners = new OwnerNaming(hostAssembly);
        var documentation = new XmlDocumentation();
        var assemblies = SolutionAssemblies.Discover(hostAssembly, modules);

        await using var factory = new DocsHostFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri(OpenApiPath, UriKind.Relative), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GET {OpenApiPath} returned {(int)response.StatusCode}.");
        }

        var openApi = await response.Content.ReadAsStringAsync(cancellationToken);
        var services = factory.Services;
        var ownAssemblies = SolutionAssemblies.OwnAssemblies(assemblies, hostAssembly);
        return new ApiSurface(
            OpenApiJson: openApi,
            Permissions: ConventionScanner.Permissions(services.GetServices<IPermissionCatalogSource>(), owners),
            Endpoints: EndpointCollector.Collect(services.GetRequiredService<EndpointDataSource>().Endpoints, owners, documentation),
            ErrorCodes: ConventionScanner.ErrorCodes(assemblies, owners, documentation),
            ConfigKeys: ConfigKeyScanner.Scan(assemblies, owners, documentation),
            Statuses: StatusScanner.Scan(ownAssemblies, owners, documentation),
            Workflows: WorkflowCollector.Collect(services.GetServices<IRecordWorkflowDefinition>(), ownAssemblies, owners));
    }
}

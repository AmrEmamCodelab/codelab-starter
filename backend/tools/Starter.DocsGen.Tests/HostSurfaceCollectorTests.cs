using Starter.DocsGen.Collection;
using Starter.DocsGen.Rendering;

namespace Starter.DocsGen.Tests;

/// <summary>Boots the real API host in-process, without a database, exactly as the tool does.</summary>
public sealed class HostSurfaceCollectorTests
{
    [Fact]
    public async Task Reads_the_real_host_without_a_database()
    {
        var surface = await new HostSurfaceCollector().CollectAsync(CancellationToken.None);

        Assert.Contains("\"/api/dashboard\"", surface.OpenApiJson, StringComparison.Ordinal);
        // A superset check: a module added by the scaffold (or a fork) adds its own keys without breaking this test.
        var keys = surface.Permissions.Select(permission => permission.Key).ToList();
        Assert.Contains("access.admin.view", keys);
        Assert.Contains("dashboard.view", keys);
        Assert.Equal(keys.Order(StringComparer.Ordinal), keys);
        AssertEndpoint(surface, "POST", "/api/auth/login", EndpointAccessKind.Anonymous);
        AssertEndpoint(surface, "POST", "/api/auth/refresh", EndpointAccessKind.Anonymous);
        AssertEndpoint(surface, "POST", "/api/auth/logout", EndpointAccessKind.Authenticated);
        AssertEndpoint(surface, "GET", "/api/me/permissions", EndpointAccessKind.Authenticated);
        AssertEndpoint(surface, "GET", "/api/features", EndpointAccessKind.Anonymous);
        AssertEndpoint(surface, "GET", "/api/dashboard", EndpointAccessKind.Permission);
        AssertEndpoint(surface, "GET", "/api/access/admin", EndpointAccessKind.Permission);
        Assert.Contains(surface.ErrorCodes, code => code.Owner == "Identity");
        Assert.Contains(surface.ConfigKeys, key => key.Key == "Codelab:Security:Protocol:Prefix");
        Assert.Contains(surface.ConfigKeys, key => key.Key == "Identity:Session:IdleTimeoutMinutes");
        Assert.Contains(surface.Statuses, status => status.Name == "ImportBatchStatus" && status.Owner == "Users");
        Assert.DoesNotContain(surface.Statuses, status => status.FullName.StartsWith(SolutionAssemblies.PlatformPackagePrefix, StringComparison.Ordinal));
        var documents = GeneratedDocSet.Render(surface);
        Assert.DoesNotContain(OpenApiNormalizer.ServersProperty, documents[GeneratedFileNames.OpenApi], StringComparison.Ordinal);
        // Zero or more workflows: the index always exists, and every registered workflow gets its sheet.
        Assert.Contains(GeneratedFileNames.WorkflowIndex, documents.Keys);
        Assert.All(surface.Workflows, workflow => Assert.Contains(GeneratedFileNames.Workflow(workflow.RecordType), documents.Keys));
    }

    private static void AssertEndpoint(ApiSurface surface, string method, string route, EndpointAccessKind access) =>
        Assert.Contains(surface.Endpoints, endpoint => endpoint.Method == method && endpoint.Route == route && endpoint.Access == access);
}

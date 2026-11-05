using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Starter.Contract.Tests.Support;
using Starter.Infrastructure.Authorization;

namespace Starter.Contract.Tests;

/// <summary>Unit tests of <see cref="EndpointAccessContract"/> over synthetic endpoints (no host).</summary>
public sealed class EndpointAccessContractTests
{
    private const string Owner = "Sample";
    private const string Permission = "sample.view";
    private static readonly string[] Owners = [Owner];

    [Fact]
    public void AnEndpointWithAPermissionPasses()
    {
        var result = EndpointAccessContract.Check([Endpoint("/a", new HasPermissionAttribute(Permission))], [], Owners);

        Assert.True(result.IsSuccessful, result.Describe());
    }

    [Fact]
    public void AnAllowlistedAnonymousEndpointPasses()
    {
        var result = EndpointAccessContract.Check([Endpoint("/a", new AllowAnonymousAttribute())], [Entry("GET /a", EndpointAccess.Anonymous)], Owners);

        Assert.True(result.IsSuccessful, result.Describe());
    }

    [Fact]
    public void AnAllowlistedAuthenticatedEndpointPasses()
    {
        var result = EndpointAccessContract.Check([Endpoint("/a", new AuthenticatedUserAttribute())], [Entry("GET /a", EndpointAccess.Authenticated)], Owners);

        Assert.True(result.IsSuccessful, result.Describe());
    }

    [Fact]
    public void AnEndpointWithoutPermissionOrEntryFails()
    {
        var result = EndpointAccessContract.Check([Endpoint("/a", new AuthenticatedUserAttribute())], [], Owners);

        Assert.Contains("GET /a: Authenticated without", Assert.Single(result.Violations), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnonymousEndpointAllowlistedAsAuthenticatedFails()
    {
        var result = EndpointAccessContract.Check([Endpoint("/a", new AllowAnonymousAttribute())], [Entry("GET /a", EndpointAccess.Authenticated)], Owners);

        Assert.Contains("allowlisted as Authenticated but is Anonymous", Assert.Single(result.Violations), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAllowlistedEndpointThatGainedAPermissionFails()
    {
        var result = EndpointAccessContract.Check([Endpoint("/a", new HasPermissionAttribute(Permission))], [Entry("GET /a", EndpointAccess.Authenticated)], Owners);

        Assert.Contains("remove the entry", Assert.Single(result.Violations), StringComparison.Ordinal);
    }

    [Fact]
    public void AStaleEntryOfAPresentOwnerFails()
    {
        var result = EndpointAccessContract.Check([Endpoint("/a", new HasPermissionAttribute(Permission))], [Entry("GET /gone", EndpointAccess.Anonymous)], Owners);

        Assert.Contains("GET /gone", Assert.Single(result.Violations), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryOfARemovedModuleIsIgnored()
    {
        var removed = new AllowlistEntry("GET /gone", EndpointAccess.Anonymous, "Removed", "test");

        var result = EndpointAccessContract.Check([Endpoint("/a", new HasPermissionAttribute(Permission))], [removed], Owners);

        Assert.True(result.IsSuccessful, result.Describe());
    }

    [Fact]
    public void NoEndpointsIsVacuousAndFails() => Assert.False(EndpointAccessContract.Check([], [], Owners).IsSuccessful);

    private static AllowlistEntry Entry(string key, EndpointAccess access) => new(key, access, Owner, "test");

    private static RouteEndpoint Endpoint(string route, object access) =>
        new(
            requestDelegate: _ => Task.CompletedTask,
            routePattern: RoutePatternFactory.Parse(route),
            order: 0,
            metadata: new EndpointMetadataCollection(new HttpMethodMetadata([HttpMethods.Get]), access),
            displayName: route);
}

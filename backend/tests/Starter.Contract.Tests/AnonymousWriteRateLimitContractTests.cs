using Codelab.Security.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Starter.Contract.Tests.Support;
using Starter.Infrastructure.Authorization;

namespace Starter.Contract.Tests;

/// <summary>Unit tests of <see cref="AnonymousWriteRateLimitContract"/> over synthetic endpoints (no host).</summary>
public sealed class AnonymousWriteRateLimitContractTests
{
    private static readonly EnableRateLimitingAttribute Limited = new(ClientRateLimitOptions.PolicyName);

    [Fact]
    public void ARateLimitedAnonymousWritePasses()
    {
        var result = AnonymousWriteRateLimitContract.Check([Endpoint(HttpMethods.Post, new AllowAnonymousAttribute(), Limited)]);

        Assert.True(result.IsSuccessful, result.Describe());
    }

    [Fact]
    public void AnAnonymousWriteWithoutARateLimitFails()
    {
        var result = AnonymousWriteRateLimitContract.Check([Endpoint(HttpMethods.Post, new AllowAnonymousAttribute())]);

        Assert.Contains("POST /a: anonymous write without", Assert.Single(result.Violations), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnonymousWriteThatDisablesTheLimitFails()
    {
        var result = AnonymousWriteRateLimitContract.Check([Endpoint(HttpMethods.Post, new AllowAnonymousAttribute(), Limited, new DisableRateLimitingAttribute())]);

        Assert.Single(result.Violations);
    }

    [Fact]
    public void AnonymousReadsAndPermissionGatedWritesAreNotExamined()
    {
        var result = AnonymousWriteRateLimitContract.Check(
        [
            Endpoint(HttpMethods.Get, new AllowAnonymousAttribute()),
            Endpoint(HttpMethods.Post, new HasPermissionAttribute("sample.edit")),
        ]);

        Assert.Empty(result.Violations);
        Assert.True(result.IsVacuous);
    }

    private static RouteEndpoint Endpoint(string method, params object[] metadata) =>
        new(
            requestDelegate: _ => Task.CompletedTask,
            routePattern: RoutePatternFactory.Parse("/a"),
            order: 0,
            metadata: new EndpointMetadataCollection([new HttpMethodMetadata([method]), .. metadata]),
            displayName: method + " /a");
}

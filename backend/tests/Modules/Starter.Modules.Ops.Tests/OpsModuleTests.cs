using System.Reflection;
using Codelab.Security.ServiceAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Starter.Application.Jobs;
using Starter.Infrastructure.Authorization;
using Starter.Modules.Ops.Api;
using Starter.Modules.Ops.Application;
using Starter.Modules.Ops.Application.Access;
using Starter.Modules.Ops.Domain;
using Starter.Modules.Ops.Infrastructure;
using Starter.Modules.Ops.Infrastructure.ReferenceCodes;
using Starter.Modules.Ops.Tests.Fakes;

namespace Starter.Modules.Ops.Tests;

public sealed class OpsModuleTests
{
    [Fact]
    public void The_module_is_named_after_its_assembly() => Assert.Equal("Ops", new OpsModule().Name);

    [Fact]
    public void Routes_live_under_the_module_root()
    {
        Assert.Equal("api/ops", OpsRoutes.Root);
        Assert.Equal("reference-codes/{code}", OpsRoutes.ReferenceCode);
        Assert.Equal("X-Codelab-Operator", OpsHeaders.Operator);
    }

    [Fact]
    public void The_controller_is_open_only_to_the_service_assertion_policy()
    {
        var controller = typeof(OpsController);
        var authorize = Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true));

        Assert.IsType<OpsServiceCallerAttribute>(authorize);
        Assert.Equal(OpsServiceCallerAttribute.PolicyName, authorize.Policy);
        Assert.Equal(ServiceAssertionAuthenticationHandler.SchemeName, authorize.AuthenticationSchemes);
        Assert.Null(controller.GetCustomAttribute<HasPermissionAttribute>());
        Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void Every_action_names_its_endpoint_and_carries_no_other_authorization()
    {
        var actions = typeof(OpsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.Equal(4, actions.Length);
        Assert.Equal(
            [OpsEndpoint.Health, OpsEndpoint.Jobs, OpsEndpoint.Certificates, OpsEndpoint.ReferenceCode],
            actions.Select(action => action.GetCustomAttribute<OpsEndpointAttribute>()!.Endpoint).Order());
        Assert.All(actions, action => Assert.Empty(action.GetCustomAttributes<AuthorizeAttribute>()));
        Assert.All(actions, action => Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>()));
    }

    [Fact]
    public void Registration_builds_the_module_with_its_policy_scheme_job_and_capture()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddOpsModule(new ConfigurationBuilder().Build());

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IJob) && descriptor.ImplementationType == typeof(Starter.Modules.Ops.Application.ReferenceCodes.ReferenceCodePurgeJob));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IStartupFilter) && descriptor.ImplementationType == typeof(ReferenceCodeCaptureStartupFilter));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IComponentProbe));
        Assert.Equal(2, services.Count(descriptor => descriptor.ServiceType == typeof(ICertificateSource)));
    }

    [Fact]
    public void Each_configured_readiness_url_adds_a_probe()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Codelab:Ops:ReadinessProbes:0:Name"] = "attachment-service",
            ["Codelab:Ops:ReadinessProbes:0:Url"] = "https://att.example.test/health/ready",
            ["Codelab:Ops:ReadinessProbes:1:Name"] = "integration-gateway",
            ["Codelab:Ops:ReadinessProbes:1:Url"] = "https://gw.example.test/health/ready",
        }).Build();

        services.AddOpsModule(configuration);

        Assert.Equal(3, services.Count(descriptor => descriptor.ServiceType == typeof(IComponentProbe)));
    }
}

public sealed class OperatorAccessFilterTests
{
    private readonly FakeAccessLog _log = new();

    [Fact]
    public async Task A_call_without_the_operator_header_gets_the_refusal_and_the_action_never_runs()
    {
        var (context, nextCalled) = await RunAsync(operatorName: null);

        Assert.False(nextCalled());
        Assert.NotNull(context.Result);
        Assert.Empty(_log.Recorded);
    }

    [Fact]
    public async Task A_call_with_the_operator_is_recorded_then_runs()
    {
        var (context, nextCalled) = await RunAsync(operatorName: "CORP\\jdoe");

        Assert.True(nextCalled());
        Assert.Null(context.Result);
        var access = Assert.Single(_log.Recorded);
        Assert.Equal(("CORP\\jdoe", "starter-ops-console", OpsEndpoint.ReferenceCode, "9C1E5-B7A20"), (access.Operator, access.CallerService, access.Endpoint, access.Subject));
        Assert.Equal(32, access.CorrelationId!.Length);
    }

    private async Task<(ActionExecutingContext Context, Func<bool> NextCalled)> RunAsync(string? operatorName)
    {
        var http = new DefaultHttpContext();
        if (operatorName is not null)
        {
            http.Request.Headers[OpsHeaders.Operator] = operatorName;
        }

        http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(Codelab.Security.Sessions.SecurityClaimTypes.Service, "starter-ops-console")],
            ServiceAssertionAuthenticationHandler.SchemeName));
        var routeData = new RouteData();
        routeData.Values[OpsRoutes.CodeParameter] = "9c1e5-b7a20";
        var descriptor = new ActionDescriptor { EndpointMetadata = [new OpsEndpointAttribute(OpsEndpoint.ReferenceCode)] };
        var context = new ActionExecutingContext(new ActionContext(http, routeData, descriptor), [], new Dictionary<string, object?>(), controller: new object());
        var called = false;
        var filter = new OperatorAccessFilter(new RecordOperatorAccess(_log, new FakeTimeProvider()));

        await filter.OnActionExecutionAsync(context, () =>
        {
            called = true;
            return Task.FromResult(new ActionExecutedContext(context, [], controller: new object()));
        });

        return (context, () => called);
    }
}

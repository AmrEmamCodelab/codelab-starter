using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Starter.Infrastructure.Authorization;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Infrastructure.Authorization;
using Starter.Modules.Access.Tests.Fakes;

namespace Starter.Modules.Access.Tests;

public sealed class PermissionHandlerTests
{
    private const string AuthenticationType = "Test";
    private static readonly Guid Subject = Guid.Parse("7f1e4c1a-3b0d-4a43-9f1e-2c6b1d1a0002");

    [Fact]
    public async Task Succeeds_when_the_caller_holds_the_key()
    {
        var context = Context(Authenticated(), new HasPermissionAttribute(AccessPermissionKeys.DashboardView));

        await new PermissionHandler(new FakeCurrentUser(Subject), new FakeResolver(AccessPermissionKeys.DashboardView)).HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Does_not_succeed_without_the_key_and_never_fails_explicitly()
    {
        var context = Context(Authenticated(), new HasPermissionAttribute(AccessPermissionKeys.DashboardView));

        await new PermissionHandler(new FakeCurrentUser(Subject), new FakeResolver(AccessPermissionKeys.AccessAdminView)).HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.False(context.HasFailed);
    }

    [Fact]
    public async Task Leaves_an_unauthenticated_caller_to_the_challenge_without_resolving()
    {
        var context = Context(new ClaimsPrincipal(new ClaimsIdentity()), new HasPermissionAttribute(AccessPermissionKeys.DashboardView));
        var resolver = new FakeResolver(AccessPermissionKeys.DashboardView);

        await new PermissionHandler(new FakeCurrentUser(Subject, isAuthenticated: false), resolver).HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task Denies_an_authenticated_caller_without_a_readable_subject()
    {
        var context = Context(Authenticated(), new HasPermissionAttribute(AccessPermissionKeys.DashboardView));
        var resolver = new FakeResolver(AccessPermissionKeys.DashboardView);

        await new PermissionHandler(new FakeCurrentUser(subjectId: null), resolver).HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task Stacked_requirements_each_need_their_own_key()
    {
        var context = Context(
            Authenticated(),
            new HasPermissionAttribute(AccessPermissionKeys.DashboardView),
            new HasPermissionAttribute(AccessPermissionKeys.AccessAdminView));

        await new PermissionHandler(new FakeCurrentUser(Subject), new FakeResolver(AccessPermissionKeys.DashboardView)).HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.Single(context.PendingRequirements);
    }

    private static ClaimsPrincipal Authenticated() => new(new ClaimsIdentity([], AuthenticationType));

    private static AuthorizationHandlerContext Context(ClaimsPrincipal user, params HasPermissionAttribute[] requirements) =>
        new(requirements, user, new DefaultHttpContext());
}

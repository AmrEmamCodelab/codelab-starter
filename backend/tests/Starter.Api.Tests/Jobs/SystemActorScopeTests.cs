using System.Net;
using System.Security.Claims;
using Codelab.Observability;
using Codelab.Security.Sessions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Starter.Application.Auditing;
using Starter.Application.Jobs;
using Starter.Infrastructure.Jobs;
using Starter.Infrastructure.Users;

namespace Starter.Api.Tests.Jobs;

public sealed class SystemActorScopeTests
{
    [Fact]
    public void Inside_a_system_scope_a_signed_in_request_reads_as_no_user()
    {
        var accessor = new HttpContextAccessor { HttpContext = Authenticated(Guid.NewGuid()) };
        var user = new HttpCurrentUserContext(accessor);

        using (new SystemActorScope().BeginSystemScope())
        {
            Assert.False(user.IsAuthenticated);
            Assert.Null(user.SubjectId);
            Assert.Null(user.SessionId);
        }

        Assert.True(user.IsAuthenticated);
        Assert.NotNull(user.SubjectId);
    }

    [Fact]
    public void Inside_a_system_scope_the_audit_actor_is_SYSTEM_even_with_a_request_in_reach()
    {
        var context = Authenticated(Guid.NewGuid());
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        var accessor = new HttpContextAccessor { HttpContext = context };
        var audit = new HttpAuditContextAccessor(accessor, new HttpCurrentUserContext(accessor));

        using (SystemActorScope.Begin())
        {
            var current = audit.Current;
            Assert.True(current.IsSystem);
            Assert.Null(current.SubjectId);
            Assert.Null(current.ClientAddress);
            Assert.Equal(CorrelationId.For(context), current.CorrelationId);
        }

        Assert.False(audit.Current.IsSystem);
    }

    [Fact]
    public void Inside_a_job_the_audit_context_carries_the_run_correlation_id()
    {
        var accessor = new HttpContextAccessor();
        var audit = new HttpAuditContextAccessor(accessor, new HttpCurrentUserContext(accessor));
        const string runCorrelationId = "5f2c9a1e7b3d4c6e8a0b2d4f6a8c0e1b";

        using (SystemActorScope.Begin())
        using (CorrelationScope.Begin(runCorrelationId))
        {
            Assert.Equal(AuditContext.System with { CorrelationId = runCorrelationId }, audit.Current);
        }
    }

    [Fact]
    public void The_system_actor_name_is_the_audit_system_actor()
    {
        Assert.Equal("SYSTEM", SystemActor.Name);
        Assert.Equal(AuditActors.System, SystemActor.Name);
    }

    [Fact]
    public void Scopes_nest_and_restore_and_a_second_dispose_is_harmless()
    {
        var scope = new SystemActorScope();
        Assert.False(scope.IsActive);

        var outer = scope.BeginSystemScope();
        var inner = scope.BeginSystemScope();
        inner.Dispose();
        Assert.True(scope.IsActive);
        outer.Dispose();
        outer.Dispose();

        Assert.False(scope.IsActive);
    }

    [Fact]
    public async Task A_scope_covers_awaited_work_and_not_the_caller_after_it()
    {
        var sawInside = false;

        await Task.Run(async () =>
        {
            using var system = SystemActorScope.Begin();
            await Task.Yield();
            sawInside = SystemActorScope.IsActiveOnCurrentFlow;
        });

        Assert.True(sawInside);
        Assert.False(SystemActorScope.IsActiveOnCurrentFlow);
    }

    [Fact]
    public async Task A_scope_on_one_flow_does_not_leak_into_a_parallel_flow()
    {
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var system = Task.Run(async () =>
        {
            using var scope = SystemActorScope.Begin();
            entered.SetResult();
            await release.Task;
        });

        await entered.Task;
        var parallel = await Task.Run(() => SystemActorScope.IsActiveOnCurrentFlow);
        release.SetResult();
        await system;

        Assert.False(parallel);
    }

    private static DefaultHttpContext Authenticated(Guid subject)
    {
        var identity = new ClaimsIdentity(
            [new Claim(SecurityClaimTypes.Subject, subject.ToString()), new Claim(SecurityClaimTypes.SessionId, "sid-1")],
            authenticationType: "test");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }
}

public sealed class JobRegistrationTests
{
    [Fact]
    public void AddStarterJob_registers_a_scoped_job_and_the_system_actor_scope_once()
    {
        var services = new ServiceCollection();

        services.AddStarterJob<SampleJob>();
        services.AddStarterJob<SampleJob>();
        services.AddStarterJob<OtherJob>();

        var jobs = services.Where(descriptor => descriptor.ServiceType == typeof(IJob)).ToList();
        Assert.Equal([typeof(SampleJob), typeof(OtherJob)], jobs.Select(descriptor => descriptor.ImplementationType));
        Assert.All(jobs, descriptor => Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime));
        var actor = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISystemActorScope));
        Assert.Equal(typeof(SystemActorScope), actor.ImplementationType);
    }

    [Fact]
    public void Each_scope_gets_its_own_job_instances()
    {
        var services = new ServiceCollection().AddStarterJob<SampleJob>();
        using var provider = services.BuildServiceProvider(validateScopes: true);

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.NotSame(first.ServiceProvider.GetServices<IJob>().Single(), second.ServiceProvider.GetServices<IJob>().Single());
    }

    private sealed class SampleJob : IJob
    {
        public string Key => "sample-job";

        public string NameAr => "مهمة";

        public string NameEn => "Sample";

        public string? Cron => null;

        public Task RunAsync(JobRunContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class OtherJob : IJob
    {
        public string Key => "other-job";

        public string NameAr => "مهمة أخرى";

        public string NameEn => "Other";

        public string? Cron => null;

        public Task RunAsync(JobRunContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

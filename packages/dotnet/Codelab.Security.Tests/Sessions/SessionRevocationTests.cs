using System.Net;
using System.Text.Json;
using Codelab.Security.Problems;
using Codelab.Security.Sessions;
using Codelab.Security.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Time.Testing;

namespace Codelab.Security.Tests.Sessions;

[Collection(SharedCodecGroup.Name)]
public sealed class SessionRevocationTests
{
    private static readonly TestSession Session = new("7", "sid-1", "m1", Version: 0);

    [Fact]
    public async Task A_revoked_sessions_next_call_is_401_session_revoked_and_other_sessions_continue()
    {
        await using var host = await StartAsync();
        Assert.Equal(HttpStatusCode.OK, (await CallAsync(host, Session)).StatusCode);

        await host.Services.GetRequiredService<ISessionRevocationStore>().RevokeAsync(new SessionRevocation(Session.SessionId, host.Clock.GetUtcNow().AddHours(1)), CancellationToken.None);

        await AssertRefusedAsync(await CallAsync(host, Session), SecurityProblemCodes.SessionRevoked);
        Assert.Equal(HttpStatusCode.OK, (await CallAsync(host, Session with { SessionId = "sid-2" })).StatusCode);
    }

    [Fact]
    public async Task A_version_bump_invalidates_every_older_session_of_the_user()
    {
        await using var host = await StartAsync();
        var newVersion = await host.Services.GetRequiredService<ISessionVersionStore>().BumpAsync(Session.UserId, CancellationToken.None);

        await AssertRefusedAsync(await CallAsync(host, Session), SecurityProblemCodes.SessionVersionStale);
        Assert.Equal(HttpStatusCode.OK, (await CallAsync(host, Session with { Version = newVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CallAsync(host, Session with { UserId = "8" })).StatusCode);
    }

    [Fact]
    public async Task A_user_token_without_a_session_id_is_refused_and_anonymous_calls_pass()
    {
        await using var host = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/ping");
        request.Headers.Add(TestAuthentication.UserHeader, "7");

        await AssertRefusedAsync(await host.Client.SendAsync(request), SecurityProblemCodes.SessionUnidentified);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/ping")).StatusCode);
    }

    [Fact]
    public async Task A_failing_store_refuses_503_rather_than_letting_the_session_through()
    {
        await using var host = await StartAsync(services => services.Replace<ISessionRevocationStore>(new FailingRevocationStore()));

        var response = await CallAsync(host, Session);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.RetryAfter is not null);
    }

    [Fact]
    public async Task The_memory_stores_keep_the_later_expiry_and_purge_expired_revocations()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero));
        var store = new MemorySessionRevocationStore(clock);
        await store.RevokeAsync(new SessionRevocation("s", clock.GetUtcNow().AddMinutes(30)), CancellationToken.None);
        await store.RevokeAsync(new SessionRevocation("s", clock.GetUtcNow().AddMinutes(1)), CancellationToken.None);

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True(await store.IsRevokedAsync("s", CancellationToken.None));
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.False(await store.IsRevokedAsync("s", CancellationToken.None));
        Assert.Equal(1, await store.PurgeExpiredAsync(CancellationToken.None));
        Assert.Equal(1, await new MemorySessionVersionStore().BumpAsync("u", CancellationToken.None));
    }

    private static async Task<SecurityTestHost> StartAsync(Action<IServiceCollection>? configure = null)
    {
        var options = SecurityTestHost.BaseOptions();
        options.MapEndpoints = endpoints => endpoints.MapGet("/api/ping", () => "pong");
        options.ConfigureServices = configure;
        return await SecurityTestHost.StartAsync(options);
    }

    private static Task<HttpResponseMessage> CallAsync(SecurityTestHost host, TestSession session)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/ping");
        session.ApplyTo(request);
        return host.Client.SendAsync(request);
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }

    private sealed class FailingRevocationStore : ISessionRevocationStore
    {
        public Task RevokeAsync(SessionRevocation revocation, CancellationToken cancellationToken) => throw new TimeoutException("store down");

        public Task<bool> IsRevokedAsync(string sessionId, CancellationToken cancellationToken) => throw new TimeoutException("store down");
    }
}

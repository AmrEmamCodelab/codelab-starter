using System.Net;
using System.Text.Json;
using Codelab.Security.Problems;
using Package.Sample.Api.Hosting;
using Package.Sample.Tests.Support;
using Xunit;

namespace Package.Sample.Tests;

/// <summary>Exit criterion: after logout the session id is revoked, so the session's next call is 401.</summary>
public sealed class SessionRevocationTests : IDisposable
{
    private const string Body = """{"title":"After logout","contactEmail":"someone@example.test"}""";
    private readonly SampleHost _host = new(SampleKeys.Create());

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task A_revoked_sessions_next_call_is_401_while_other_sessions_continue()
    {
        var session = _host.NewSession();
        var sameUserOtherSession = _host.NewSession(session.UserId);
        Assert.Equal(HttpStatusCode.OK, (await session.SendAsync(HttpMethod.Post, SampleRoutes.Records, Body)).Response.StatusCode);

        var logout = await session.SendAsync(HttpMethod.Post, SampleRoutes.Logout);
        var next = await session.SendAsync(HttpMethod.Post, SampleRoutes.Records, Body);

        Assert.Equal(HttpStatusCode.NoContent, logout.Response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, next.Response.StatusCode);
        using var problem = JsonDocument.Parse(await next.Response.Content.ReadAsStringAsync());
        Assert.Equal(SecurityProblemCodes.SessionRevoked, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await sameUserOtherSession.SendAsync(HttpMethod.Post, SampleRoutes.Records, Body)).Response.StatusCode);
    }
}

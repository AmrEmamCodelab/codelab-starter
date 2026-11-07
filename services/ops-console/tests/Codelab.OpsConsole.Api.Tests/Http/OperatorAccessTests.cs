using System.Net;
using System.Text.Json;
using Codelab.OpsConsole.Api.Tests.TestSupport;

namespace Codelab.OpsConsole.Api.Tests.Http;

/// <summary>Who may use the console: Windows-signed-in members of the operator group only.</summary>
public sealed class OperatorAccessTests : IClassFixture<OpsConsoleHostFixture>
{
    private readonly OpsConsoleHost _host;

    public OperatorAccessTests(OpsConsoleHostFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _host = fixture.Host;
    }

    [Theory]
    [InlineData("/ops-api/me")]
    [InlineData("/ops-api/health")]
    [InlineData("/ops-api/jobs")]
    [InlineData("/ops-api/certificates")]
    [InlineData("/ops-api/reference-codes/9C1E5-B7A20")]
    public async Task An_unauthenticated_caller_gets_401(string path)
    {
        using var client = _host.CreateClient();
        using var request = OpsConsoleHost.OperatorRequest(path, name: null);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/ops-api/me")]
    [InlineData("/ops-api/health")]
    [InlineData("/ops-api/reference-codes/9C1E5-B7A20")]
    public async Task A_signed_in_user_outside_the_operator_group_gets_403_and_reaches_no_upstream(string path)
    {
        using var client = _host.CreateClient();
        using var request = OpsConsoleHost.OperatorRequest(path, "CORP\\mallory", "S-1-5-21-1000-2000-3000-9999", "CORP\\Domain Users");
        var callsBefore = _host.Api.Calls.Count;

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", await CodeOf(response));
        Assert.Equal(callsBefore, _host.Api.Calls.Count);
    }

    [Fact]
    public async Task An_operator_sees_their_name_and_the_operator_group()
    {
        using var client = _host.CreateClient();
        using var request = OpsConsoleHost.OperatorRequest("/ops-api/me", "CORP\\alice");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("CORP\\alice", body.RootElement.GetProperty("name").GetString());
        Assert.Equal(OpsConsoleHost.OperatorGroupSid, body.RootElement.GetProperty("groups")[0].GetString());
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_need_no_operator(string path)
    {
        using var client = _host.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_route_is_closed_by_the_fallback_policy()
    {
        using var client = _host.CreateClient();
        using var request = OpsConsoleHost.OperatorRequest("/ops-api/unknown", name: null);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString();
    }
}

using System.Net;
using System.Text.Json;
using Codelab.OpsConsole.Api.Tests.TestSupport;
using Codelab.OpsConsole.Api.Upstream;
using Codelab.Security.ServiceAuth;

namespace Codelab.OpsConsole.Api.Tests.Http;

/// <summary>
/// The console against a fake /api/ops that runs the real ServiceAssertionValidator: every call carries a fresh,
/// valid, single-use assertion bound to its method and path, and names the operator.
/// </summary>
public sealed class UpstreamCallTests : IDisposable
{
    private const string Operator = "CORP\\alice";

    private readonly OpsConsoleHost _host = new();

    public void Dispose() => _host.Dispose();

    [Theory]
    [InlineData("/ops-api/health", "api/ops/health", "{\"status\":\"Healthy\",\"checks\":[{\"name\":\"sql\",\"status\":\"Healthy\"}]}")]
    [InlineData("/ops-api/jobs", "api/ops/jobs", "[{\"jobKey\":\"outbox-drain\",\"lastRunStatus\":2}]")]
    public async Task Health_and_jobs_pass_the_api_answer_through(string path, string apiRoute, string apiJson)
    {
        _host.Api.Answer(apiRoute, apiJson);
        using var client = _host.CreateClient();
        using var request = OpsConsoleHost.OperatorRequest(path, Operator);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Normalize(apiJson), Normalize(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Every_api_call_carries_a_valid_assertion_and_the_operator_name()
    {
        _host.Api.Answer(OpsApiRoutes.Health, "{}");
        using var client = _host.CreateClient();

        for (var call = 0; call < 2; call++)
        {
            using var request = OpsConsoleHost.OperatorRequest("/ops-api/health", Operator);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var calls = _host.Api.Calls.ToArray();
        Assert.Equal(2, calls.Length);
        Assert.All(calls, received =>
        {
            Assert.Equal(ServiceAssertionOutcome.Valid, received.Outcome);
            Assert.Equal("/api/ops/health", received.PathAndQuery);
            Assert.Equal(Operator, received.Operator);
        });
        Assert.NotEqual(calls[0].Assertion, calls[1].Assertion);
    }

    [Fact]
    public async Task A_captured_assertion_is_refused_when_replayed_or_moved_to_another_route()
    {
        _host.Api.Answer(OpsApiRoutes.Health, "{}");
        using var client = _host.CreateClient();
        using var request = OpsConsoleHost.OperatorRequest("/ops-api/health", Operator);
        using var response = await client.SendAsync(request);
        var captured = Assert.Single(_host.Api.Calls).Assertion;

        var replayed = await _host.Api.ValidateAsync(captured, "GET", new Uri(OpsConsoleHost.ApiBaseUrl + OpsApiRoutes.Health));
        var moved = await _host.Api.ValidateAsync(captured, "GET", new Uri(OpsConsoleHost.ApiBaseUrl + OpsApiRoutes.Jobs));

        Assert.Equal(ServiceAssertionOutcome.Replayed, replayed.Outcome);
        Assert.Equal(ServiceAssertionOutcome.BindingMismatch, moved.Outcome);
    }

    [Fact]
    public async Task Certificates_keep_the_contract_shape_with_the_integer_status()
    {
        _host.Api.Answer(OpsApiRoutes.Certificates, "[{\"name\":\"idp-signing\",\"subject\":\"CN=idp.example.test\",\"notAfter\":\"2026-11-28T00:00:00+00:00\",\"daysLeft\":21,\"status\":2}]");
        using var client = _host.CreateClient();
        using var request = OpsConsoleHost.OperatorRequest("/ops-api/certificates", Operator);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var certificate = Assert.Single(body.RootElement.EnumerateArray());
        Assert.Equal("idp-signing", certificate.GetProperty("name").GetString());
        Assert.Equal("CN=idp.example.test", certificate.GetProperty("subject").GetString());
        Assert.Equal(21, certificate.GetProperty("daysLeft").GetInt32());
        Assert.Equal(2, certificate.GetProperty("status").GetInt32());
        Assert.Equal(new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero), certificate.GetProperty("notAfter").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task An_api_failure_is_502_upstream_unavailable(HttpStatusCode apiStatus)
    {
        _host.Api.Answer(OpsApiRoutes.Jobs, "{}", apiStatus);
        using var client = _host.CreateClient();
        using var request = OpsConsoleHost.OperatorRequest("/ops-api/jobs", Operator);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("UPSTREAM_UNAVAILABLE", await CodeOf(response));
    }

    [Fact]
    public async Task An_unreachable_api_is_502_upstream_unavailable()
    {
        _host.Api.Unreachable = true;
        using var client = _host.CreateClient();
        using var request = OpsConsoleHost.OperatorRequest("/ops-api/health", Operator);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("UPSTREAM_UNAVAILABLE", await CodeOf(response));
    }

    internal static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString();
    }

    private static string Normalize(string json) => JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement);
}

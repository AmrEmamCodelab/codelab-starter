using System.Net;
using System.Text.Json;
using Codelab.OpsConsole.Api.Tests.TestSupport;
using Codelab.OpsConsole.Api.Upstream;
using Codelab.Security.ServiceAuth;

namespace Codelab.OpsConsole.Api.Tests.Http;

/// <summary>GET /ops-api/reference-codes/{code}: the API's facts merged with the Seq events of the same request.</summary>
public sealed class ReferenceCodeLookupTests : IDisposable
{
    private const string Code = "9C1E5-B7A20";
    private const string CorrelationId = "9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b";

    private readonly OpsConsoleHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Seq_events_alone_trace_the_request_with_a_seq_link()
    {
        _host.Seq.EventsJson = "[" + string.Join(',',
            FakeSeq.Event("2026-11-07T10:00:01.5000000Z", "Information", "HTTP GET /api/records responded 500",
                ("CorrelationId", CorrelationId), ("RequestMethod", "GET"), ("RequestPath", "/api/records"), ("StatusCode", 500)),
            FakeSeq.Event("2026-11-07T10:00:01.2000000Z", "Error", "Request failed", ("CorrelationId", CorrelationId))) + "]";
        using var client = _host.CreateClient();
        using var request = OpsConsoleHost.OperatorRequest("/ops-api/reference-codes/9c1e5-b7a20");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(Code, root.GetProperty("referenceCode").GetString());
        Assert.Equal(CorrelationId, root.GetProperty("correlationId").GetString());
        Assert.Equal(CorrelationId, root.GetProperty("traceId").GetString());
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 10, 0, 1, 200, TimeSpan.Zero), root.GetProperty("firstSeen").GetDateTimeOffset());
        Assert.Equal("GET", root.GetProperty("request").GetProperty("method").GetString());
        Assert.Equal("/api/records", root.GetProperty("request").GetProperty("path").GetString());
        Assert.Equal(500, root.GetProperty("request").GetProperty("status").GetInt32());
        Assert.Equal(["Error", "Information"], root.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("level").GetString()));
        Assert.StartsWith(OpsConsoleHost.SeqBaseUrl + "#/events?filter=", root.GetProperty("seqUrl").GetString(), StringComparison.Ordinal);
        Assert.True(root.GetProperty("seqAvailable").GetBoolean());
    }

    [Fact]
    public async Task Seq_is_asked_with_the_read_key_and_only_the_normalized_code()
    {
        using var client = _host.CreateClient();
        using var request = OpsConsoleHost.OperatorRequest("/ops-api/reference-codes/" + CorrelationId);

        using var response = await client.SendAsync(request);

        var seqCall = Assert.Single(_host.Seq.Calls);
        Assert.Equal(OpsConsoleHost.SeqApiKey, seqCall.ApiKey);
        Assert.Equal("/api/events", seqCall.Uri.AbsolutePath);
        Assert.Contains(Uri.EscapeDataString("ReferenceCode = '" + Code + "'"), seqCall.Uri.Query, StringComparison.Ordinal);
        var apiCall = Assert.Single(_host.Api.Calls);
        Assert.Equal("/api/ops/reference-codes/" + Code, apiCall.PathAndQuery);
        Assert.Equal(ServiceAssertionOutcome.Valid, apiCall.Outcome);
    }

    [Fact]
    public async Task The_api_facts_win_and_seq_down_still_answers_without_events()
    {
        _host.Api.Answer(OpsApiRoutes.ReferenceCode(Code), $"{{\"referenceCode\":\"{Code}\",\"correlationId\":\"{CorrelationId}\",\"traceId\":\"aaaabbbbccccddddeeeeffff00001111\",\"firstSeen\":\"2026-11-07T09:59:59Z\",\"request\":{{\"method\":\"POST\",\"path\":\"/api/service-requests\",\"status\":409}}}}");
        _host.Seq.Unreachable = true;
        using var client = _host.CreateClient();
        using var request = OpsConsoleHost.OperatorRequest("/ops-api/reference-codes/" + Code);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("aaaabbbbccccddddeeeeffff00001111", root.GetProperty("traceId").GetString());
        Assert.Equal(409, root.GetProperty("request").GetProperty("status").GetInt32());
        Assert.Empty(root.GetProperty("events").EnumerateArray());
        Assert.False(root.GetProperty("seqAvailable").GetBoolean());
    }

    [Theory]
    [InlineData("not-a-code")]
    [InlineData("9C1E5'B7A20")]
    [InlineData("9C1E5-B7A2Z")]
    public async Task A_malformed_code_is_400_and_reaches_neither_upstream(string code)
    {
        using var client = _host.CreateClient();
        using var request = OpsConsoleHost.OperatorRequest("/ops-api/reference-codes/" + Uri.EscapeDataString(code));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("OPS_REFERENCE_CODE_INVALID", await UpstreamCallTests.CodeOf(response));
        Assert.Empty(_host.Api.Calls);
        Assert.Empty(_host.Seq.Calls);
    }

    [Fact]
    public async Task A_code_nobody_knows_is_404()
    {
        using var client = _host.CreateClient();
        using var request = OpsConsoleHost.OperatorRequest("/ops-api/reference-codes/" + Code);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("OPS_REFERENCE_CODE_NOT_FOUND", await UpstreamCallTests.CodeOf(response));
    }

    [Fact]
    public async Task An_unknown_code_while_seq_refuses_the_key_is_502_not_a_false_404()
    {
        _host.Seq.Status = HttpStatusCode.Unauthorized;
        using var client = _host.CreateClient();
        using var request = OpsConsoleHost.OperatorRequest("/ops-api/reference-codes/" + Code);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("UPSTREAM_UNAVAILABLE", await UpstreamCallTests.CodeOf(response));
    }
}

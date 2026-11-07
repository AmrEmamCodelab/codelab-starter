using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Serilog.Events;
using Starter.Api.Tests.Support;
using Starter.Infrastructure.Security;

namespace Starter.Api.Tests.SecurityEdge;

/// <summary>The CSP report sink over HTTP on the composed kernel host (TestServer, no database).</summary>
[Collection(HostTestGroup.Name)]
public sealed class CspReportSinkHttpTests
{
    private const string ProblemJson = "application/problem+json";
    private static readonly Uri Sink = new("/" + SecurityEdgeRoutes.CspReports, UriKind.Relative);

    private const string ReportUriBody = """
        {"csp-report":{"document-uri":"https://portal.example.test/requests?nationalId=9876543210#tab",
        "blocked-uri":"https://cdn.example.test/lib.js?token=secret","effective-directive":"script-src-elem",
        "violated-directive":"script-src-elem 'self'","disposition":"report","original-policy":"default-src 'self'",
        "referrer":"https://portal.example.test/home?q=private","script-sample":"alert('private')",
        "source-file":"https://portal.example.test/main.js?v=3","line-number":12,"column-number":7,"status-code":200}}
        """;

    private const string ReportingApiBody = """
        [{"type":"csp-violation","age":10,"url":"https://portal.example.test/a?x=1","user_agent":"test",
          "body":{"documentURL":"https://portal.example.test/a?x=1","blockedURL":"inline","effectiveDirective":"style-src-attr",
          "disposition":"enforce","statusCode":200}},
         {"type":"deprecation","body":{"id":"x"}}]
        """;

    [Fact]
    public async Task A_report_uri_report_is_accepted_and_logged_as_one_redacted_CspViolation_event()
    {
        var sink = new CapturingSink();
        await using var app = await SecurityEdgeHost.StartAsync(sink);
        using var client = app.GetTestClient();

        using var response = await client.PostAsync(Sink, Body(ReportUriBody, CspReportContract.ReportUriMediaType));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var logged = Assert.Single(CspEvents(sink));
        Assert.Equal(LogEventLevel.Warning, logged.Level);
        Assert.Equal("script-src-elem", Scalar(logged, CspViolationLog.DirectiveProperty));
        Assert.Equal("https://portal.example.test/requests", Scalar(logged, CspViolationLog.DocumentUriProperty));
        Assert.Equal("https://cdn.example.test/lib.js", Scalar(logged, CspViolationLog.BlockedUriProperty));
        Assert.Equal("https://portal.example.test/main.js", Scalar(logged, CspViolationLog.SourceFileProperty));
        Assert.Equal(12, Scalar(logged, CspViolationLog.LineNumberProperty));
        var rendered = logged.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + string.Join('|', logged.Properties.Values);
        Assert.DoesNotContain("9876543210", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("private", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reporting_api_batch_logs_its_csp_violations_only()
    {
        var sink = new CapturingSink();
        await using var app = await SecurityEdgeHost.StartAsync(sink);
        using var client = app.GetTestClient();

        using var response = await client.PostAsync(Sink, Body(ReportingApiBody, CspReportContract.ReportingApiMediaType));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var logged = Assert.Single(CspEvents(sink));
        Assert.Equal("style-src-attr", Scalar(logged, CspViolationLog.DirectiveProperty));
        Assert.Equal("inline", Scalar(logged, CspViolationLog.BlockedUriProperty));
    }

    [Fact]
    public async Task A_body_over_the_ceiling_is_a_413_problem_and_logs_nothing()
    {
        var sink = new CapturingSink();
        await using var app = await SecurityEdgeHost.StartAsync(sink);
        using var client = app.GetTestClient();
        var oversized = ReportUriBody.Replace("lib.js", "lib.js" + new string('a', CspReportContract.MaxBodyBytes), StringComparison.Ordinal);

        using var response = await client.PostAsync(Sink, Body(oversized, CspReportContract.ReportUriMediaType));

        await AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "PAYLOAD_TOO_LARGE");
        Assert.Empty(CspEvents(sink));
    }

    [Fact]
    public async Task A_chunked_body_over_the_ceiling_is_a_413_problem()
    {
        var sink = new CapturingSink();
        await using var app = await SecurityEdgeHost.StartAsync(sink);
        using var client = app.GetTestClient();
        using var content = new StreamContent(new MemoryStream(new byte[CspReportContract.MaxBodyBytes + 1]));
        content.Headers.ContentType = new MediaTypeHeaderValue(CspReportContract.ReportUriMediaType);
        content.Headers.ContentLength = null;
        using var request = new HttpRequestMessage(HttpMethod.Post, Sink) { Content = content };
        request.Headers.TransferEncodingChunked = true;

        using var response = await client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "PAYLOAD_TOO_LARGE");
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("text/plain")]
    public async Task Another_content_type_is_a_415_problem(string contentType)
    {
        var sink = new CapturingSink();
        await using var app = await SecurityEdgeHost.StartAsync(sink);
        using var client = app.GetTestClient();

        using var response = await client.PostAsync(Sink, Body(ReportUriBody, contentType));

        await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType, "MALFORMED_REQUEST");
        Assert.Empty(CspEvents(sink));
    }

    [Fact]
    public async Task A_body_that_is_not_a_report_is_a_400_problem()
    {
        var sink = new CapturingSink();
        await using var app = await SecurityEdgeHost.StartAsync(sink);
        using var client = app.GetTestClient();

        using var response = await client.PostAsync(Sink, Body("{\"not\":\"a report\"}", CspReportContract.ReportUriMediaType));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "MALFORMED_REQUEST");
    }

    [Fact]
    public async Task The_sink_is_rate_limited_per_client()
    {
        var sink = new CapturingSink();
        await using var app = await SecurityEdgeHost.StartAsync(sink, new Dictionary<string, string?>
        {
            ["Codelab:Security:RateLimiting:PermitLimit"] = "2",
            ["Codelab:Security:RateLimiting:WindowSeconds"] = "600",
        });
        using var client = app.GetTestClient();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await client.PostAsync(Sink, Body(ReportUriBody, CspReportContract.ReportUriMediaType));
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.TooManyRequests], statuses);
    }

    private static StringContent Body(string text, string contentType)
    {
        var content = new StringContent(text, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return content;
    }

    private static List<LogEvent> CspEvents(CapturingSink sink) =>
        [.. sink.Events.Where(logEvent => logEvent.Properties.TryGetValue("EventId", out var id)
            && id is StructureValue structure
            && structure.Properties.Any(property => property.Name == "Name" && property.Value is ScalarValue { Value: CspViolationLog.EventName }))];

    private static object? Scalar(LogEvent logEvent, string property) =>
        Assert.IsType<ScalarValue>(logEvent.Properties[property]).Value;

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        Assert.True(body.RootElement.TryGetProperty("referenceCode", out _));
    }
}

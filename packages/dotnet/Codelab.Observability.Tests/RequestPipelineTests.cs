using System.Diagnostics;
using System.Security.Claims;
using Codelab.Observability.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace Codelab.Observability.Tests;

/// <summary>
/// The request half end to end: every response echoes the correlation id, every line carries the correlation id and
/// reference code, the request line has no query string, the user appears only as a hash, and logs are PII-free.
/// </summary>
public sealed class RequestPipelineTests
{
    private const string RecordsPath = "/records/{id}";

    public sealed record Applicant([property: Pii(PiiKind.Email)] string Email, [property: Pii(PiiKind.Phone)] string Phone, string Reference);

    [Fact]
    public async Task Logs_are_pii_free_and_carry_correlation_id_and_reference_code()
    {
        var (app, sink) = await TestHosts.StartAsync(TestHosts.Pipeline(MapApplicantEndpoint));
        await using var _ = app;

        using var response = await app.GetTestClient().GetAsync(new Uri("/records/7?search=jane@example.test", UriKind.Relative));
        var correlationId = response.Headers.GetValues(CorrelationId.Header).Single();

        var requestLine = await sink.WaitForAsync(IsRequestLine);
        var applicantLine = sink.Events.Single(e => e.MessageTemplate.Text.StartsWith("Applicant", StringComparison.Ordinal));
        foreach (var line in new[] { requestLine, applicantLine })
        {
            Assert.Equal($"\"{correlationId}\"", line.Properties[LogPropertyNames.CorrelationId].ToString());
            Assert.Equal($"\"{ReferenceCode.From(correlationId)}\"", line.Properties[LogPropertyNames.ReferenceCode].ToString());
        }

        Assert.Equal($"\"{RecordsPath}\"", requestLine.Properties[LogPropertyNames.RouteTemplate].ToString());
        var text = sink.AllText();
        Assert.DoesNotContain("jane@example.test", text, StringComparison.Ordinal);
        // The random correlation id (and its reference code) can contain any digits; only the rest of the text must not.
        var textWithoutIds = text.Replace(correlationId, string.Empty, StringComparison.Ordinal)
            .Replace(ReferenceCode.From(correlationId), string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("7946", textWithoutIds, StringComparison.Ordinal);
        Assert.Contains("j***@example.test", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_user_is_logged_as_a_hash_and_never_as_the_raw_identifier()
    {
        var (app, sink) = await TestHosts.StartAsync(TestHosts.Pipeline(MapApplicantEndpoint));
        await using var _ = app;

        using var response = await app.GetTestClient().GetAsync(new Uri("/records/9", UriKind.Relative));

        var expected = app.Services.GetRequiredService<UserIdHash>().Of(UserId);
        var applicantLine = sink.Events.Single(e => e.MessageTemplate.Text.StartsWith("Applicant", StringComparison.Ordinal));
        Assert.Equal($"\"{expected}\"", applicantLine.Properties[LogPropertyNames.UserIdHash].ToString());
        Assert.DoesNotContain(UserId, sink.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_requests_are_quiet_and_liveness_runs_no_check()
    {
        var spec = TestHosts.Pipeline(app =>
        {
            app.UseCodelabObservability();
            app.MapCodelabHealth();
        }) with
        { ConfigureServices = services => services.AddHealthChecks().AddCheck("db", FailingCheck, tags: [HealthEndpoints.ReadyTag]) };
        var (app, sink) = await TestHosts.StartAsync(spec);
        await using var _ = app;
        var client = app.GetTestClient();

        using var live = await client.GetAsync(new Uri(HealthEndpoints.LivePath, UriKind.Relative));
        using var ready = await client.GetAsync(new Uri(HealthEndpoints.ReadyPath, UriKind.Relative));

        Assert.Equal(StatusCodes.Status200OK, (int)live.StatusCode);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, (int)ready.StatusCode);
        using var other = await client.GetAsync(new Uri("/elsewhere", UriKind.Relative));
        await sink.WaitForAsync(IsRequestLine);
        Assert.DoesNotContain(sink.Events, e => IsRequestLine(e) && e.Properties["RequestPath"].ToString().Contains(HealthEndpoints.BasePath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_caller_supplied_traceparent_never_sets_the_correlation_id_or_the_trace()
    {
        const string plantedTraceId = "9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b";
        const string tracePath = "/trace";
        var (app, _) = await TestHosts.StartAsync(TestHosts.Pipeline(web =>
        {
            web.UseCodelabObservability();
            web.MapGet(tracePath, () => Results.Text(Activity.Current?.TraceId.ToHexString() ?? string.Empty));
        }));
        await using var _app = app;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(tracePath, UriKind.Relative));
        request.Headers.Add(PropagationScope.TraceParentHeader, $"00-{plantedTraceId}-b7ad6b7169203331-01");

        using var response = await app.GetTestClient().SendAsync(request);
        var correlationId = response.Headers.GetValues(CorrelationId.Header).Single();
        var serverTraceId = await response.Content.ReadAsStringAsync();

        Assert.NotEqual(plantedTraceId, correlationId);
        Assert.NotEqual(ReferenceCode.From(plantedTraceId), ReferenceCode.From(correlationId));
        Assert.NotEqual(plantedTraceId, serverTraceId);
    }

    [Fact]
    public void Hosting_resolves_the_inbound_propagator_that_ignores_every_header()
    {
        var services = new ServiceCollection();
        services.AddSingleton(DistributedContextPropagator.CreateDefaultPropagator());

        ObservabilityBootstrap.IgnoreInboundTraceContext(services);

        using var provider = services.BuildServiceProvider();
        Assert.Same(PropagationScope.InboundPropagator, provider.GetRequiredService<DistributedContextPropagator>());
        PropagationScope.InboundPropagator.ExtractTraceIdAndState(
            carrier: null,
            static (object? _, string _, out string? value, out IEnumerable<string>? values) =>
            {
                value = "00-9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b-b7ad6b7169203331-01";
                values = null;
            },
            out var traceId,
            out var traceState);
        Assert.Null(traceId);
        Assert.Null(traceState);
        Assert.Null(PropagationScope.InboundPropagator.ExtractBaggage(carrier: null, getter: null));
    }

    [Theory]
    [InlineData("/health/live", false, LogEventLevel.Verbose)]
    [InlineData("/records/1", false, LogEventLevel.Information)]
    [InlineData("/records/1", true, LogEventLevel.Error)]
    public void Request_line_level_depends_on_path_and_exception(string path, bool failed, LogEventLevel expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        var level = CodelabObservabilityExtensions.LevelFor(
            context, failed ? new InvalidOperationException() : null, [new PathString(HealthEndpoints.BasePath)]);

        Assert.Equal(expected, level);
    }

    private const string UserId = "user-4711";

    /// <summary>The Serilog request line: the only event with both the request path and the elapsed time.</summary>
    private static bool IsRequestLine(LogEvent logEvent) =>
        logEvent.Properties.ContainsKey("RequestPath") && logEvent.Properties.ContainsKey("Elapsed");

    private static Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult FailingCheck() =>
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy();

    private static void MapApplicantEndpoint(WebApplication app)
    {
        app.Use((context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId)], "test"));
            return next(context);
        });
        app.UseCodelabObservability();
        app.UseCodelabUserLogContext();
        app.MapGet(RecordsPath, (string id, ILogger<RequestPipelineTests> logger) =>
        {
            var applicant = new Applicant("jane@example.test", "+44 20 7946 0958", "REF-1");
            TestLog.Applicant(logger, applicant, id);
            return Results.Ok();
        });
    }
}

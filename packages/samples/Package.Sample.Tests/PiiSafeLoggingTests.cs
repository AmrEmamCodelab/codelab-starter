using System.Net;
using Codelab.Observability;
using Codelab.Security.PublicIds;
using Codelab.Testing.Logging;
using Package.Sample.Api.Hosting;
using Package.Sample.Api.Records;
using Package.Sample.Tests.Support;
using Serilog.Events;
using Xunit;

namespace Package.Sample.Tests;

/// <summary>
/// Exit criterion: logs are free of personal data and carry the correlation id and reference code the caller saw.
/// Planted canaries (the user id and the contact e-mail) must never reach any sink; the Codelab.Testing canary sink
/// sits in DI, so it gets exactly what the retained sinks (file, Seq) would get.
/// </summary>
public sealed class PiiSafeLoggingTests : IDisposable
{
    private const string EmailDomain = "@example.test";
    private readonly string _userCanary = CanaryLogSink.NewCanary("user");
    private readonly string _emailCanary = CanaryLogSink.NewCanary("contact");
    private readonly CanaryLogSink _sink;
    private readonly UntrustedTlsServer _upstream;
    private readonly SampleHost _host;

    public PiiSafeLoggingTests()
    {
        _sink = new CanaryLogSink([_userCanary, _emailCanary]);
        _upstream = UntrustedTlsServer.Start();
        _host = new SampleHost(SampleKeys.Create(), settings => SampleSettings.PointArchiveAt(settings, _upstream.BaseAddress), _sink);
    }

    public void Dispose()
    {
        _host.Dispose();
        _upstream.Dispose();
    }

    [Fact]
    public async Task A_request_line_carries_the_correlation_id_reference_code_and_user_hash_but_no_personal_data()
    {
        var session = _host.NewSession(_userCanary);
        var email = _emailCanary + EmailDomain;

        var exchange = await session.SendAsync(HttpMethod.Post, SampleRoutes.Records, $$"""{"title":"Logged record","contactEmail":"{{email}}"}""");

        Assert.Equal(HttpStatusCode.OK, exchange.Response.StatusCode);
        var correlationId = exchange.Response.Headers.GetValues(CorrelationId.Header).Single();
        var mine = EventsWith(LogPropertyNames.CorrelationId, correlationId);
        Assert.NotEmpty(mine);
        Assert.All(mine, logEvent => Assert.Equal(ReferenceCode.From(correlationId), Value(logEvent, LogPropertyNames.ReferenceCode)));
        Assert.Contains(mine, logEvent => !string.IsNullOrEmpty(Value(logEvent, LogPropertyNames.UserIdHash)));
        Assert.Contains(mine, logEvent => Value(logEvent, "ContactEmailMasked") == PiiMask.Email(email));
        _sink.Check().ThrowIfFailed();
    }

    [Fact]
    public async Task A_failure_line_carries_the_reference_code_shown_in_the_error_body()
    {
        var session = _host.NewSession(_userCanary);
        var path = SampleRoutes.ArchiveFor(PublicId<RecordIds>.From(RecordStore.SeedRecordId).ToToken());

        var exchange = await session.SendAsync(HttpMethod.Post, path);

        var problem = await Problems.ReadAsync(exchange.Response, HttpStatusCode.BadGateway);
        var correlationId = problem.GetProperty("correlationId").GetString()!;
        var referenceCode = problem.GetProperty("referenceCode").GetString()!;
        Assert.Equal(ReferenceCode.From(correlationId), referenceCode);
        Assert.Equal(correlationId, exchange.Response.Headers.GetValues(CorrelationId.Header).Single());
        var failures = EventsWith(LogPropertyNames.ReferenceCode, referenceCode).Where(logEvent => logEvent.Level >= LogEventLevel.Warning).ToList();
        Assert.NotEmpty(failures);
        Assert.All(failures, logEvent => Assert.Equal(correlationId, Value(logEvent, LogPropertyNames.CorrelationId)));
        _sink.Check().ThrowIfFailed();
    }

    [Fact]
    public async Task Every_line_carries_the_apis_release_not_the_test_runners()
    {
        var expected = ReleaseVersion.Of(typeof(Program).Assembly);

        var exchange = await _host.NewSession(_userCanary).SendAsync(HttpMethod.Get, SampleRoutes.RecordFor(PublicId<RecordIds>.From(RecordStore.SeedRecordId).ToToken()));

        var correlationId = exchange.Response.Headers.GetValues(CorrelationId.Header).Single();
        var mine = EventsWith(LogPropertyNames.CorrelationId, correlationId);
        Assert.NotEmpty(mine);
        Assert.All(mine, logEvent => Assert.Equal(expected, Value(logEvent, LogPropertyNames.Release)));
        Assert.NotEqual(ReleaseVersion.Of(System.Reflection.Assembly.GetEntryAssembly()!), expected);
    }

    private List<LogEvent> EventsWith(string property, string value) =>
        _sink.Events.Where(logEvent => Value(logEvent, property) == value).ToList();

    private static string? Value(LogEvent logEvent, string property) =>
        logEvent.Properties.TryGetValue(property, out var value) && value is ScalarValue scalar ? scalar.Value?.ToString() : null;
}

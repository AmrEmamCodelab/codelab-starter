using System.Text.Json;
using Codelab.OpsConsole.Application.Certificates;
using Codelab.OpsConsole.Application.Ports;
using Codelab.OpsConsole.Application.ReferenceCodes;

namespace Codelab.OpsConsole.Application.Tests.ReferenceCodes;

public sealed class LookUpReferenceCodeTests
{
    private const string Code = "9C1E5-B7A20";
    private const string CorrelationId = "9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b";
    private const string OtherCorrelationId = "9c1e5b7a20ffffffffffffffffffffff";

    private static readonly OperatorName Alice = new("CORP\\alice");
    private static readonly Uri SeqLink = new("https://seq.example.test/#/events");
    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_invalid_input_asks_nobody()
    {
        var api = new FakeApi(facts: null);
        var logs = new FakeLogs(LogSearchResult.Found([], SeqLink));

        var lookup = await new LookUpReferenceCode(api, logs).ExecuteAsync("nope", Alice, CancellationToken.None);

        Assert.Equal(ReferenceCodeLookupOutcome.Invalid, lookup.Outcome);
        Assert.Null(lookup.Trace);
        Assert.Equal(0, api.Calls);
        Assert.Equal(0, logs.Calls);
    }

    [Fact]
    public async Task Both_sides_are_asked_with_the_normalized_code_and_the_operator()
    {
        var api = new FakeApi(new ApiReferenceCodeFacts { CorrelationId = CorrelationId });
        var logs = new FakeLogs(LogSearchResult.Found([], SeqLink));

        await new LookUpReferenceCode(api, logs).ExecuteAsync(CorrelationId, Alice, CancellationToken.None);

        Assert.Equal(Code, api.LastCode);
        Assert.Equal(Alice, api.LastOperator);
        Assert.Equal(Code, logs.LastCode);
    }

    [Fact]
    public async Task Unknown_to_both_is_not_found()
    {
        var lookup = await Run(facts: null, LogSearchResult.Found([], SeqLink));

        Assert.Equal(ReferenceCodeLookupOutcome.NotFound, lookup.Outcome);
    }

    [Fact]
    public async Task Unknown_to_the_api_while_logs_are_down_is_unknown_not_not_found()
    {
        var lookup = await Run(facts: null, LogSearchResult.Unavailable(SeqLink));

        Assert.Equal(ReferenceCodeLookupOutcome.LogsUnavailable, lookup.Outcome);
    }

    [Fact]
    public async Task Api_facts_alone_are_a_trace_that_says_the_logs_were_unavailable()
    {
        var request = new TracedRequest("POST", "/api/x", 409);
        var facts = new ApiReferenceCodeFacts { CorrelationId = CorrelationId, FirstSeen = T0, Request = request };

        var trace = (await Run(facts, LogSearchResult.Unavailable(link: null))).Trace!;

        Assert.Equal(Code, trace.ReferenceCode);
        Assert.Equal(CorrelationId, trace.TraceId);
        Assert.Equal(T0, trace.FirstSeen);
        Assert.Equal(request, trace.Request);
        Assert.Empty(trace.Events);
        Assert.False(trace.LogsAvailable);
        Assert.Null(trace.LogsLink);
    }

    [Fact]
    public async Task Log_events_alone_fill_every_fact_oldest_first()
    {
        var requestLine = Event(T0.AddMilliseconds(500), CorrelationId) with { Request = new TracedRequest("GET", "/api/records", 500), TraceId = "trace-1" };
        var failure = Event(T0.AddMilliseconds(200), CorrelationId) with { Level = "Error" };

        var trace = (await Run(facts: null, LogSearchResult.Found([requestLine, failure], SeqLink))).Trace!;

        Assert.Equal(CorrelationId, trace.CorrelationId);
        Assert.Equal("trace-1", trace.TraceId);
        Assert.Equal(T0.AddMilliseconds(200), trace.FirstSeen);
        Assert.Equal(requestLine.Request, trace.Request);
        Assert.Equal(["Error", "Information"], trace.Events.Select(logged => logged.Level));
        Assert.True(trace.LogsAvailable);
        Assert.Equal(SeqLink, trace.LogsLink);
    }

    [Fact]
    public async Task The_api_facts_win_over_the_log_events()
    {
        var facts = new ApiReferenceCodeFacts { CorrelationId = CorrelationId, TraceId = "api-trace", FirstSeen = T0, Request = new TracedRequest("POST", "/a", 400) };
        var logged = Event(T0.AddSeconds(1), CorrelationId) with { TraceId = "log-trace", Request = new TracedRequest("GET", "/b", 200) };

        var trace = (await Run(facts, LogSearchResult.Found([logged], SeqLink))).Trace!;

        Assert.Equal("api-trace", trace.TraceId);
        Assert.Equal(T0, trace.FirstSeen);
        Assert.Equal("/a", trace.Request!.Path);
        Assert.Single(trace.Events);
    }

    [Fact]
    public async Task Events_of_another_request_sharing_the_code_are_left_out()
    {
        var mine = Event(T0.AddSeconds(2), CorrelationId);
        var other = Event(T0.AddSeconds(1), OtherCorrelationId);
        var unlabelled = Event(T0.AddSeconds(3), correlationId: null);
        var facts = new ApiReferenceCodeFacts { CorrelationId = CorrelationId.ToUpperInvariant() };

        var trace = (await Run(facts, LogSearchResult.Found([mine, other, unlabelled], SeqLink))).Trace!;

        Assert.Equal([mine, unlabelled], trace.Events);
    }

    [Fact]
    public async Task Without_api_facts_the_earliest_request_wins_a_shared_code()
    {
        var earliest = Event(T0.AddSeconds(1), OtherCorrelationId);
        var later = Event(T0.AddSeconds(2), CorrelationId);

        var trace = (await Run(facts: null, LogSearchResult.Found([later, earliest], SeqLink))).Trace!;

        Assert.Equal(OtherCorrelationId, trace.CorrelationId);
        Assert.Equal([earliest], trace.Events);
    }

    [Fact]
    public async Task An_api_failure_propagates()
    {
        var api = new FakeApi(facts: null) { Failure = new InvalidOperationException("api down") };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LookUpReferenceCode(api, new FakeLogs(LogSearchResult.Found([], SeqLink))).ExecuteAsync(Code, Alice, CancellationToken.None));
    }

    private static Task<ReferenceCodeLookup> Run(ApiReferenceCodeFacts? facts, LogSearchResult logs) =>
        new LookUpReferenceCode(new FakeApi(facts), new FakeLogs(logs)).ExecuteAsync(Code, Alice, CancellationToken.None);

    private static LoggedEvent Event(DateTimeOffset timestamp, string? correlationId) =>
        new() { Timestamp = timestamp, Level = "Information", Message = "m", CorrelationId = correlationId };

    private sealed class FakeApi(ApiReferenceCodeFacts? facts) : IOpsApiClient
    {
        public int Calls { get; private set; }

        public string? LastCode { get; private set; }

        public OperatorName? LastOperator { get; private set; }

        public Exception? Failure { get; init; }

        public Task<ApiReferenceCodeFacts?> FindReferenceCodeAsync(string referenceCode, OperatorName operatorName, CancellationToken cancellationToken)
        {
            Calls++;
            LastCode = referenceCode;
            LastOperator = operatorName;
            return Failure is null ? Task.FromResult(facts) : Task.FromException<ApiReferenceCodeFacts?>(Failure);
        }

        public Task<JsonElement> GetHealthAsync(OperatorName operatorName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<JsonElement> GetJobsAsync(OperatorName operatorName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CertificateExpiry>> GetCertificatesAsync(OperatorName operatorName, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeLogs(LogSearchResult result) : ILogEventSource
    {
        public int Calls { get; private set; }

        public string? LastCode { get; private set; }

        public Task<LogSearchResult> FindByReferenceCodeAsync(string referenceCode, CancellationToken cancellationToken)
        {
            Calls++;
            LastCode = referenceCode;
            return Task.FromResult(result);
        }
    }
}

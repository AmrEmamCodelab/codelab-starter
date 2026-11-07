using System.Net;
using System.Text.Json;
using Codelab.OpsConsole.Api.Hosting;
using Codelab.OpsConsole.Api.Tests.TestSupport;
using Codelab.OpsConsole.Api.Upstream;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Codelab.OpsConsole.Api.Tests.Upstream;

public sealed class SeqUnitTests
{
    private const string Code = "9C1E5-B7A20";

    [Fact]
    public void The_filter_names_the_reference_code_property_and_refuses_a_quote()
    {
        Assert.Equal("ReferenceCode = '9C1E5-B7A20'", SeqQueries.ReferenceCodeFilter(Code));
        Assert.Throws<ArgumentException>(() => SeqQueries.ReferenceCodeFilter("9C1E5'B7A20"));
    }

    [Fact]
    public void The_events_request_is_bounded_in_count_and_time_and_rendered()
    {
        var uri = SeqQueries.EventsRequest(SeqQueries.ReferenceCodeFilter(Code), count: 50, new DateTimeOffset(2026, 10, 7, 8, 30, 0, TimeSpan.FromHours(3)));

        Assert.False(uri.IsAbsoluteUri);
        Assert.Equal(
            "api/events?filter=ReferenceCode%20%3D%20%279C1E5-B7A20%27&count=50&render=true&fromDateUtc=2026-10-07T05%3A30%3A00.000Z",
            uri.OriginalString);
    }

    [Fact]
    public void The_ui_link_keeps_a_base_path()
    {
        var link = SeqQueries.UiLink(new Uri("https://ops.example.test/seq/"), "ReferenceCode = 'A'");

        Assert.Equal("https://ops.example.test/seq/#/events?filter=ReferenceCode%20%3D%20%27A%27", link.AbsoluteUri);
    }

    [Fact]
    public void The_reader_maps_seq_events_and_skips_ones_without_a_timestamp()
    {
        var json = "[" + string.Join(',',
            FakeSeq.Event("2026-11-07T10:00:01.5Z", "Warning", "HTTP GET /x responded 404",
                ("CorrelationId", "abc"), ("RequestMethod", "GET"), ("RequestPath", "/x"), ("StatusCode", 404)),
            "{\"Timestamp\":\"2026-11-07T10:00:02Z\",\"TraceId\":\"t-1\",\"Properties\":[{\"Name\":\"RequestMethod\",\"Value\":\"GET\"}]}",
            "{\"Level\":\"Error\"}",
            "42") + "]";

        var events = SeqEventReader.Read(JsonDocument.Parse(json).RootElement);

        Assert.Equal(2, events.Count);
        Assert.Equal("Warning", events[0].Level);
        Assert.Equal("abc", events[0].CorrelationId);
        Assert.Equal(new Application.ReferenceCodes.TracedRequest("GET", "/x", 404), events[0].Request);
        Assert.Equal(SeqEventReader.DefaultLevel, events[1].Level);
        Assert.Equal(string.Empty, events[1].Message);
        Assert.Equal("t-1", events[1].TraceId);
        Assert.Null(events[1].Request);
    }

    [Fact]
    public void The_reader_refuses_a_body_that_is_not_an_array() =>
        Assert.Throws<JsonException>(() => SeqEventReader.Read(JsonDocument.Parse("{}").RootElement));

    [Fact]
    public async Task Without_a_seq_address_nothing_is_called()
    {
        var seq = new FakeSeq();
        var source = Source(seq, new SeqReadOptions());

        var result = await source.FindByReferenceCodeAsync(Code, CancellationToken.None);

        Assert.False(result.Available);
        Assert.Null(result.Link);
        Assert.Empty(seq.Calls);
    }

    [Fact]
    public async Task The_read_key_is_sent_only_when_configured()
    {
        var seq = new FakeSeq();

        await Source(seq, new SeqReadOptions { BaseUrl = "https://seq.example.test/", ApiKey = "k" }).FindByReferenceCodeAsync(Code, CancellationToken.None);
        await Source(seq, new SeqReadOptions { BaseUrl = "https://seq.example.test/" }).FindByReferenceCodeAsync(Code, CancellationToken.None);

        Assert.Equal(["k", null], seq.Calls.Select(call => call.ApiKey));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "[]", false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "[]", false)]
    [InlineData(HttpStatusCode.OK, "not json", false)]
    [InlineData(HttpStatusCode.OK, "{}", false)]
    [InlineData(HttpStatusCode.OK, "[]", true)]
    public async Task Seq_failures_are_unavailable_never_exceptions(HttpStatusCode status, string body, bool available)
    {
        var seq = new FakeSeq { Status = status, EventsJson = body };

        var result = await Source(seq, new SeqReadOptions { BaseUrl = "https://seq.example.test/" }).FindByReferenceCodeAsync(Code, CancellationToken.None);

        Assert.Equal(available, result.Available);
        Assert.NotNull(result.Link);
    }

    [Fact]
    public async Task An_unreachable_seq_is_unavailable()
    {
        var seq = new FakeSeq { Unreachable = true };

        var result = await Source(seq, new SeqReadOptions { BaseUrl = "https://seq.example.test/" }).FindByReferenceCodeAsync(Code, CancellationToken.None);

        Assert.False(result.Available);
    }

    [Fact]
    public async Task The_look_back_starts_from_the_injected_clock()
    {
        var seq = new FakeSeq();
        var options = new SeqReadOptions { BaseUrl = "https://seq.example.test/", LookbackDays = 2, MaxEvents = 7 };

        await Source(seq, options, new FakeTimeProvider(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero))).FindByReferenceCodeAsync(Code, CancellationToken.None);

        var query = Assert.Single(seq.Calls).Uri.Query;
        Assert.Contains("count=7", query, StringComparison.Ordinal);
        Assert.Contains("fromDateUtc=2026-11-05T12%3A00%3A00.000Z", query, StringComparison.Ordinal);
    }

    private static SeqLogEventSource Source(FakeSeq seq, SeqReadOptions seqOptions, TimeProvider? clock = null)
    {
        var http = new HttpClient(seq.CreateHandler());
        if (seqOptions.IsConfigured)
        {
            http.BaseAddress = UpstreamAddress.AsBaseAddress(seqOptions.BaseUrl!);
        }

        var options = new OpsConsoleOptions { Seq = seqOptions };
        return new SeqLogEventSource(http, new StaticOptions(options), clock ?? TimeProvider.System, NullLogger<SeqLogEventSource>.Instance);
    }

    private sealed class StaticOptions(OpsConsoleOptions value) : IOptionsMonitor<OpsConsoleOptions>
    {
        public OpsConsoleOptions CurrentValue => value;

        public OpsConsoleOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<OpsConsoleOptions, string?> listener) => null;
    }
}

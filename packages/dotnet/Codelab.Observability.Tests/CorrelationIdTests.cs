using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Codelab.Observability.Tests;

/// <summary>One id per request, owned by the server; inbound ids are recorded, never adopted.</summary>
public sealed partial class CorrelationIdTests
{
    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex ThirtyTwoHex();

    [Fact]
    public void Uses_current_activity_trace_id()
    {
        using var activity = new Activity("request").Start();

        Assert.Equal(activity.TraceId.ToHexString(), CorrelationId.For(new DefaultHttpContext()));
    }

    [Fact]
    public void Uses_the_hosting_activity_over_a_later_current_activity()
    {
        using var hosting = new Activity("hosting").Start();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpActivityFeature>(new ActivityFeature(hosting));

        using var inner = new Activity("inner");
        inner.SetParentId(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom());
        inner.Start();

        Assert.Equal(hosting.TraceId.ToHexString(), CorrelationId.For(context));
    }

    [Fact]
    public void Mints_32_hex_when_no_activity_and_keeps_it_for_the_request()
    {
        Activity.Current = null;
        var context = new DefaultHttpContext();

        var id = CorrelationId.For(context);

        Assert.Matches(ThirtyTwoHex(), id);
        Assert.Equal(id, CorrelationId.For(context));
        Assert.NotEqual(id, CorrelationId.For(new DefaultHttpContext()));
    }

    [Fact]
    public void Reads_the_innermost_scope_outside_a_request()
    {
        Activity.Current = null;
        Assert.Null(CorrelationId.Current(null));

        using (CorrelationScope.Begin("9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b"))
        {
            using (CorrelationScope.Begin("5d2c9e8f1a3b4c6d7e8f9a0b1c2d3e4f"))
            {
                Assert.Equal("5d2c9e8f1a3b4c6d7e8f9a0b1c2d3e4f", CorrelationId.Current(null));
            }

            Assert.Equal("9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b", CorrelationId.Current(null));
        }

        Assert.Null(CorrelationScope.Current);
    }

    [Fact]
    public void Disposing_a_scope_twice_restores_only_once()
    {
        using var outer = CorrelationScope.Begin("outer-id-0001");
        var inner = CorrelationScope.Begin("inner-id-0002");
        inner.Dispose();
        inner.Dispose();

        Assert.Equal("outer-id-0001", CorrelationScope.Current);
    }

    [Fact]
    public void Outside_a_request_and_any_scope_the_activity_trace_id_is_used()
    {
        using var job = new Activity("job").Start();

        Assert.Equal(job.TraceId.ToHexString(), CorrelationId.Current(null));
    }

    [Fact]
    public void A_scope_rejects_an_id_that_could_not_come_from_a_request()
    {
        Assert.Throws<ArgumentException>(() => CorrelationScope.Begin("forged\r\nline"));
    }

    [Fact]
    public void Differing_inbound_header_is_kept_only_as_the_inbound_id()
    {
        using var activity = new Activity("request").Start();
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationId.Header] = "caller-chosen-id-123";

        Assert.Equal(activity.TraceId.ToHexString(), CorrelationId.For(context));
        Assert.Equal("caller-chosen-id-123", CorrelationId.InboundFor(context));
    }

    [Fact]
    public void An_inbound_header_equal_to_the_id_is_not_recorded_twice()
    {
        using var activity = new Activity("request").Start();
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationId.Header] = activity.TraceId.ToHexString();

        Assert.Null(CorrelationId.InboundFor(context));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("has space in it")]
    [InlineData("line\r\nbreak-injection")]
    [InlineData("a123456789b123456789c123456789d123456789e123456789f123456789g123456789")]
    public void Malformed_inbound_header_is_ignored(string inbound)
    {
        using var activity = new Activity("request").Start();
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationId.Header] = inbound;

        Assert.Equal(activity.TraceId.ToHexString(), CorrelationId.For(context));
        Assert.Null(CorrelationId.InboundFor(context));
    }

    [Fact]
    public void A_repeated_inbound_header_is_ignored()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationId.Header] = new(["first-id-0001", "second-id-0002"]);

        Assert.Null(CorrelationId.InboundFor(context));
    }

    private sealed class ActivityFeature(Activity activity) : IHttpActivityFeature
    {
        public Activity Activity { get; set; } = activity;
    }
}

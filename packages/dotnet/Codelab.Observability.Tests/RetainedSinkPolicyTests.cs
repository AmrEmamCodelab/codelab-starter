using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace Codelab.Observability.Tests;

/// <summary>The default retained-sink predicate drops the framework lines that carry query strings or SQL text.</summary>
public sealed class RetainedSinkPolicyTests
{
    [Theory]
    [InlineData(RetainedSinkPolicy.HostingDiagnosticsSource, true)]
    [InlineData(RetainedSinkPolicy.DatabaseCommandSource, true)]
    [InlineData("Starter.Modules.Records", false)]
    public void Unsafe_sources_are_recognised(string source, bool expected)
    {
        Assert.Equal(expected, RetainedSinkPolicy.IsUnsafeByDefault(EventFrom(source)));
    }

    [Fact]
    public void An_event_without_a_source_is_safe()
    {
        Assert.False(RetainedSinkPolicy.IsUnsafeByDefault(EventFrom(source: null)));
    }

    [Fact]
    public void It_is_the_default_predicate()
    {
        var options = ObservabilityOptions.For("svc");

        Assert.True(options.IsUnsafeForRetainedSinks(EventFrom(RetainedSinkPolicy.HostingDiagnosticsSource)));
        Assert.Equal("svc", options.ServiceName);
    }

    private static LogEvent EventFrom(string? source)
    {
        var properties = source is null
            ? []
            : new[] { new LogEventProperty(Constants.SourceContextPropertyName, new ScalarValue(source)) };
        return new LogEvent(
            TimeProvider.System.GetUtcNow(), LogEventLevel.Information, exception: null,
            new MessageTemplateParser().Parse("Line"), properties);
    }
}

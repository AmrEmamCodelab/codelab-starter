using Serilog;
using Serilog.Events;
using Codelab.Observability.Tests.Support;

namespace Codelab.Observability.Tests;

/// <summary>Objects destructured into a log event have their [Pii] properties masked.</summary>
public sealed class PiiMaskingDestructuringPolicyTests
{
    public sealed record Contact(
        [property: Pii(PiiKind.Email)] string Email,
        [property: Pii(PiiKind.Phone)] string Phone,
        [property: Pii] string FullName,
        [property: Pii] int? Secret,
        string Reference);

    public sealed record Plain(string Name, int Count);

    [Fact]
    public void Marked_properties_are_masked_and_the_rest_is_kept()
    {
        var sink = new CaptureSink();
        using var logger = LoggerWith(sink);

        logger.Information("Saved {@Contact}", new Contact("jane@example.test", "+44 20 7946 0958", "Jane Doe", 42, "REF-7"));

        var text = sink.AllText();
        Assert.Contains("j***@example.test", text, StringComparison.Ordinal);
        Assert.Contains("***58", text, StringComparison.Ordinal);
        Assert.Contains("REF-7", text, StringComparison.Ordinal);
        Assert.DoesNotContain("jane@example.test", text, StringComparison.Ordinal);
        Assert.DoesNotContain("7946", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Jane Doe", text, StringComparison.Ordinal);
        Assert.DoesNotContain("42", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_marked_value_stays_null()
    {
        var sink = new CaptureSink();
        using var logger = LoggerWith(sink);

        logger.Information("Saved {@Contact}", new Contact("jane@example.test", "+44 20 7946 0958", "Jane Doe", null, "REF-8"));

        var structure = Assert.IsType<StructureValue>(sink.Events.Single().Properties["Contact"]);
        var secret = Assert.IsType<ScalarValue>(structure.Properties.Single(p => p.Name == nameof(Contact.Secret)).Value);
        Assert.Null(secret.Value);
    }

    [Fact]
    public void A_type_without_marked_properties_is_left_to_the_default()
    {
        var policy = new PiiMaskingDestructuringPolicy();
        var sink = new CaptureSink();
        using var logger = LoggerWith(sink);

        logger.Information("Saved {@Plain}", new Plain("widget", 3));

        Assert.False(policy.TryDestructure(new Plain("widget", 3), new NoFactory(), out _));
        Assert.Contains("widget", sink.AllText(), StringComparison.Ordinal);
    }

    private static Serilog.Core.Logger LoggerWith(CaptureSink sink) =>
        new LoggerConfiguration().Destructure.With(new PiiMaskingDestructuringPolicy()).WriteTo.Sink(sink).CreateLogger();

    private sealed class NoFactory : Serilog.Core.ILogEventPropertyValueFactory
    {
        public LogEventPropertyValue CreatePropertyValue(object? value, bool destructureObjects = false) => new ScalarValue(value);
    }
}

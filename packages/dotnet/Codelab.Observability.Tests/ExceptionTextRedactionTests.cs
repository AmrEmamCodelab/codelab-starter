using Codelab.Observability.Tests.Support;
using Serilog;
using Serilog.Events;

namespace Codelab.Observability.Tests;

/// <summary>Values quoted by database error text never reach a retained sink; the rest of the text stays.</summary>
public sealed class ExceptionTextRedactionTests
{
    [Theory]
    [InlineData(
        "Cannot insert duplicate key row in object 'dbo.Users' with unique index 'IX_Email'. The duplicate key value is (jane@example.test).",
        "Cannot insert duplicate key row in object 'dbo.Users' with unique index 'IX_Email'. The duplicate key value is (***).")]
    [InlineData(
        "String or binary data would be truncated in table 'dbo.Users', column 'Email'. Truncated value: 'jane@exa'.",
        "String or binary data would be truncated in table 'dbo.Users', column 'Email'. Truncated value: '***'.")]
    [InlineData(
        "Conversion failed when converting the nvarchar value 'jane@example.test' to data type int.",
        "Conversion failed when converting the nvarchar value '***' to data type int.")]
    [InlineData(
        "The duplicate key value is (a, b (c)).",
        "The duplicate key value is (***).")]
    public void Quoted_values_are_masked(string text, string expected)
    {
        Assert.Equal(expected, ExceptionTextRedaction.Redact(text));
    }

    [Fact]
    public void Text_without_a_quoted_value_is_returned_as_the_same_instance()
    {
        const string text = "Timeout expired.";

        Assert.Same(text, ExceptionTextRedaction.Redact(text));
    }

    [Fact]
    public void A_wrapped_sink_redacts_the_exception_and_nested_string_properties()
    {
        var sink = new CaptureSink();
        using var logger = new LoggerConfiguration()
            .WriteTo.Sink(ExceptionTextRedaction.Wrap(sink))
            .CreateLogger();
        var failure = new InvalidOperationException("The duplicate key value is (jane@example.test).");

        logger.Error(failure, "Save failed {Error} {@Details}", failure.ToString(), new { Inner = failure.Message });

        var logged = sink.Events.Single();
        Assert.DoesNotContain("jane@example.test", sink.AllText(), StringComparison.Ordinal);
        Assert.Contains("The duplicate key value is (***)", logged.Exception!.ToString(), StringComparison.Ordinal);
        Assert.Equal(failure.StackTrace, logged.Exception.StackTrace);
    }

    [Fact]
    public void An_event_without_a_quoted_value_keeps_its_exception()
    {
        var sink = new CaptureSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(ExceptionTextRedaction.Wrap(sink)).CreateLogger();
        var failure = new TimeoutException("Timeout expired.");

        logger.Error(failure, "Query failed");

        Assert.Same(failure, sink.Events.Single().Exception);
    }

    [Fact]
    public void Sequences_and_dictionaries_are_redacted_too()
    {
        var value = new SequenceValue([new ScalarValue("The duplicate key value is (x).")]);
        var dictionary = new DictionaryValue([KeyValuePair.Create(new ScalarValue("k"), (LogEventPropertyValue)new ScalarValue("Truncated value: 'y'."))]);

        Assert.Equal("[\"The duplicate key value is (***).\"]", ExceptionTextRedaction.RedactValue(value).ToString());
        Assert.Contains("'***'", ExceptionTextRedaction.RedactValue(dictionary).ToString(), StringComparison.Ordinal);
    }
}

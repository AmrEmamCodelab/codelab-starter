namespace Codelab.Observability.Tests;

/// <summary>The structured state carries named values plus the template, and renders invariantly.</summary>
public sealed class StructuredLogStateTests
{
    [Fact]
    public void Exposes_values_then_the_original_format()
    {
        var state = new StructuredLogState("Job {JobId} failed {Retry}", [
            KeyValuePair.Create("JobId", (object?)"17"),
            KeyValuePair.Create("Retry", (object?)2),
        ]);

        Assert.Equal(3, state.Count);
        Assert.Equal(KeyValuePair.Create("JobId", (object?)"17"), state[0]);
        Assert.Equal(StructuredLogState.OriginalFormatKey, state[2].Key);
        Assert.Equal("Job {JobId} failed {Retry}", state[2].Value);
        Assert.Equal(["JobId", "Retry", StructuredLogState.OriginalFormatKey], state.Select(p => p.Key));
    }

    [Fact]
    public void Formats_by_replacing_each_placeholder()
    {
        var state = new StructuredLogState("Value {Amount} for {Name}", [
            KeyValuePair.Create("Amount", (object?)1.5),
            KeyValuePair.Create("Name", (object?)null),
        ]);

        Assert.Equal("Value 1.5 for ", StructuredLogState.Format(state, null));
    }

    [Fact]
    public void A_blank_template_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new StructuredLogState(" ", []));
    }
}

using System.Collections.Concurrent;
using System.Globalization;
using Codelab.Testing.Checks;
using Serilog.Core;
using Serilog.Events;

namespace Codelab.Testing.Logging;

/// <summary>
/// A Serilog sink for PII-leak tests. Plant canary values (a fake national id, e-mail, name) in the input of the code
/// under test, log through this sink, then <see cref="Check"/> that no canary reached any rendered message, template,
/// property (nested structures and sequences included) or exception text.
/// </summary>
public sealed class CanaryLogSink : ILogEventSink
{
    private const string CanaryPrefix = "canary";
    private readonly IReadOnlyList<string> _canaries;
    private readonly ConcurrentQueue<LogEvent> _events = new();

    /// <summary>Creates a sink watching for <paramref name="canaries"/>.</summary>
    public CanaryLogSink(IEnumerable<string> canaries)
    {
        ArgumentNullException.ThrowIfNull(canaries);
        _canaries = [.. canaries.Where(canary => !string.IsNullOrEmpty(canary))];
        if (_canaries.Count == 0)
        {
            throw new ArgumentException("At least one canary value is required.", nameof(canaries));
        }
    }

    /// <summary>Every event the sink received, in order.</summary>
    public IReadOnlyList<LogEvent> Events => [.. _events];

    /// <summary>A unique canary value with a readable label, for example <c>canary-email-3f2a…</c>.</summary>
    public static string NewCanary(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return $"{CanaryPrefix}-{label}-{Guid.NewGuid():N}";
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        _events.Enqueue(logEvent);
    }

    /// <summary>Every place a canary appeared. The canary value itself is not repeated in the report.</summary>
    public IReadOnlyList<CanaryLeak> FindLeaks()
    {
        var leaks = new List<CanaryLeak>();
        var index = 0;
        foreach (var logEvent in _events)
        {
            leaks.AddRange(LeaksIn(logEvent, index));
            index++;
        }

        return leaks;
    }

    /// <summary>A check that fails on any leak, and as vacuous when nothing was logged.</summary>
    public CheckResult Check() =>
        new("No canary value reached the logs", FindLeaks().Select(leak => leak.ToString()), _events.Count);

    private IEnumerable<CanaryLeak> LeaksIn(LogEvent logEvent, int eventIndex)
    {
        var template = logEvent.MessageTemplate.Text;
        foreach (var (location, text) in RenderedParts(logEvent))
        {
            foreach (var canaryIndex in Enumerable.Range(0, _canaries.Count).Where(i => text.Contains(_canaries[i], StringComparison.Ordinal)))
            {
                yield return new CanaryLeak(eventIndex, template, location, canaryIndex);
            }
        }
    }

    private static IEnumerable<(string Location, string Text)> RenderedParts(LogEvent logEvent)
    {
        yield return (CanaryLeak.MessageLocation, logEvent.RenderMessage(CultureInfo.InvariantCulture));
        yield return (CanaryLeak.TemplateLocation, logEvent.MessageTemplate.Text);
        foreach (var property in logEvent.Properties)
        {
            yield return (CanaryLeak.PropertyLocation + property.Key, property.Value.ToString(format: null, CultureInfo.InvariantCulture));
        }

        if (logEvent.Exception is not null)
        {
            yield return (CanaryLeak.ExceptionLocation, logEvent.Exception.ToString());
        }
    }
}

/// <summary>Where a canary leaked.</summary>
/// <param name="EventIndex">The 0-based index of the log event.</param>
/// <param name="MessageTemplate">The event's message template (to find the log call).</param>
/// <param name="Location"><c>message</c>, <c>template</c>, <c>property:&lt;name&gt;</c> or <c>exception</c>.</param>
/// <param name="CanaryIndex">The index of the canary in the list the sink was created with.</param>
public sealed record CanaryLeak(int EventIndex, string MessageTemplate, string Location, int CanaryIndex)
{
    /// <summary>The rendered message.</summary>
    public const string MessageLocation = "message";

    /// <summary>The message template text.</summary>
    public const string TemplateLocation = "template";

    /// <summary>The prefix of a property location.</summary>
    public const string PropertyLocation = "property:";

    /// <summary>The exception text.</summary>
    public const string ExceptionLocation = "exception";

    /// <inheritdoc />
    public override string ToString() => $"event {EventIndex} (\"{MessageTemplate}\") leaks canary #{CanaryIndex} in {Location}";
}

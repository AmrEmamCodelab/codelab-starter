using System.Collections.Concurrent;
using System.Globalization;
using Serilog.Events;
using Serilog.Core;

namespace Codelab.Observability.Tests.Support;

/// <summary>Collects every event a retained sink receives; registered in DI (read by <c>ReadFrom.Services</c>).</summary>
public sealed class CaptureSink : ILogEventSink
{
    private const int PollAttempts = 50;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyList<LogEvent> Events => [.. _events];

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

    /// <summary>Waits briefly for an event matching <paramref name="predicate"/> (the request line is written after the response).</summary>
    public async Task<LogEvent> WaitForAsync(Func<LogEvent, bool> predicate)
    {
        for (var attempt = 0; attempt < PollAttempts; attempt++)
        {
            if (Events.FirstOrDefault(predicate) is { } found)
            {
                return found;
            }

            await Task.Delay(PollInterval);
        }

        throw new Xunit.Sdk.XunitException("No captured event matched.");
    }

    /// <summary>Every rendered message, exception text and property value, for "this value never appears" assertions.</summary>
    public string AllText() => string.Join(
        Environment.NewLine,
        Events.Select(e => string.Join(
            " | ",
            e.RenderMessage(CultureInfo.InvariantCulture),
            e.Exception?.ToString() ?? string.Empty,
            string.Join(";", e.Properties.Select(p => $"{p.Key}={p.Value}")))));
}

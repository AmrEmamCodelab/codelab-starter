using Microsoft.Extensions.Logging;
using Starter.Infrastructure.Security;

namespace Starter.Api.Tests.SecurityEdge;

public sealed class CspViolationLogTests
{
    [Fact]
    public void Write_logs_one_warning_named_CspViolation_with_every_field_as_a_property()
    {
        var logger = new RecordingLogger();
        var violation = new CspViolation
        {
            Format = CspReportFormat.ReportingApi,
            Directive = "script-src",
            Disposition = CspDisposition.Report,
            DocumentUri = "https://portal.example.test/a",
            BlockedUri = "inline",
            SourceFile = "https://portal.example.test/main.js",
            LineNumber = 4,
            ColumnNumber = 2,
            StatusCode = 200,
        };

        CspViolationLog.Write(logger, violation);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(CspViolationLog.EventIdValue, entry.EventId.Id);
        Assert.Equal(CspViolationLog.EventName, entry.EventId.Name);
        Assert.Equal("script-src", entry.Properties[CspViolationLog.DirectiveProperty]);
        Assert.Equal(CspDisposition.Report, entry.Properties[CspViolationLog.DispositionProperty]);
        Assert.Equal("inline", entry.Properties[CspViolationLog.BlockedUriProperty]);
        Assert.Equal("https://portal.example.test/a", entry.Properties[CspViolationLog.DocumentUriProperty]);
        Assert.Equal("https://portal.example.test/main.js", entry.Properties[CspViolationLog.SourceFileProperty]);
        Assert.Equal(4, entry.Properties[CspViolationLog.LineNumberProperty]);
        Assert.Equal(2, entry.Properties[CspViolationLog.ColumnNumberProperty]);
        Assert.Equal(200, entry.Properties[CspViolationLog.StatusCodeProperty]);
        Assert.Equal(CspReportFormat.ReportingApi, entry.Properties[CspViolationLog.FormatProperty]);
        Assert.Equal("CSP violation of script-src (Report): inline on https://portal.example.test/a", entry.Message);
    }

    private sealed record Entry(LogLevel Level, EventId EventId, IReadOnlyDictionary<string, object?> Properties, string Message);

    private sealed class RecordingLogger : ILogger
    {
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : [];
            Entries.Add(new Entry(logLevel, eventId, properties, formatter(state, exception)));
        }
    }
}

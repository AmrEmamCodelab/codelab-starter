using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace Codelab.Observability;

/// <summary>
/// Removes the data values an exception's TEXT can quote before a retained sink writes it. SQL Server quotes the
/// offending value in three messages: the unique-key violation (<c>The duplicate key value is (...)</c>), the
/// truncation error (<c>Truncated value: '...'</c>) and the failed conversion (<c>converting the varchar value '...'
/// to data type</c>). With a unique index on an e-mail or national id, that value is personal data.
/// <para>
/// Only the value is replaced: the type, the rest of the message (index, table, column) and the stack stay. The
/// exception text and every string property (at any depth) are redacted, because some framework events pass
/// <c>exception.ToString()</c> again as a plain string argument.
/// </para>
/// </summary>
public static partial class ExceptionTextRedaction
{
    /// <summary>What a redacted value reads as.</summary>
    public const string Mask = "***";

    private const string QuotedValueMarker = "value";

    /// <summary><paramref name="text"/> with every quoted value replaced by <see cref="Mask"/>; the same instance when nothing matched.</summary>
    public static string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!text.Contains(QuotedValueMarker, StringComparison.Ordinal))
        {
            return text;
        }

        var redacted = DuplicateKeyValue().Replace(text, $"${{prefix}}({Mask})");
        redacted = TruncatedValue().Replace(redacted, $"${{prefix}}'{Mask}'");
        redacted = ConvertedValue().Replace(redacted, $"${{prefix}}'{Mask}'");
        return string.Equals(redacted, text, StringComparison.Ordinal) ? text : redacted;
    }

    /// <summary>Wraps <paramref name="inner"/> so every event it receives has its exception text and string properties redacted.</summary>
    public static ILogEventSink Wrap(ILogEventSink inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return new RedactingSink(inner);
    }

    // Greedy to the LAST ")." on the line: a composite key's value is "(a, b)", and a value may itself hold ")".
    [GeneratedRegex(@"(?<prefix>The duplicate key value is )\(.*\)(?=\.)", RegexOptions.CultureInvariant)]
    private static partial Regex DuplicateKeyValue();

    [GeneratedRegex(@"(?<prefix>Truncated value: )'.*'(?=\.)", RegexOptions.CultureInvariant)]
    private static partial Regex TruncatedValue();

    [GeneratedRegex(@"(?<prefix>converting the (?:n?varchar|n?char|sql_variant) value )'.*'(?= to data type)", RegexOptions.CultureInvariant)]
    private static partial Regex ConvertedValue();

    internal static LogEventPropertyValue RedactValue(LogEventPropertyValue value) => value switch
    {
        ScalarValue { Value: string text } => ReferenceEquals(Redact(text), text) ? value : new ScalarValue(Redact(text)),
        SequenceValue sequence => new SequenceValue(sequence.Elements.Select(RedactValue)),
        StructureValue structure => new StructureValue(
            structure.Properties.Select(p => new LogEventProperty(p.Name, RedactValue(p.Value))), structure.TypeTag),
        DictionaryValue dictionary => new DictionaryValue(
            dictionary.Elements.Select(e => KeyValuePair.Create(e.Key, RedactValue(e.Value)))),
        _ => value,
    };

    private sealed class RedactingSink(ILogEventSink inner) : ILogEventSink, IDisposable
    {
        public void Emit(LogEvent logEvent)
        {
            var exception = logEvent.Exception;
            if (exception is not null)
            {
                var text = exception.ToString();
                var rendered = Redact(text);
                exception = ReferenceEquals(text, rendered) ? exception : new RedactedException(exception, rendered);
            }

            inner.Emit(new LogEvent(
                logEvent.Timestamp,
                logEvent.Level,
                exception,
                logEvent.MessageTemplate,
                logEvent.Properties.Select(p => new LogEventProperty(p.Key, RedactValue(p.Value))),
                logEvent.TraceId ?? default,
                logEvent.SpanId ?? default));
        }

        public void Dispose() => (inner as IDisposable)?.Dispose();
    }

    /// <summary>Stands in for the original in a sink: <see cref="ToString"/> is the redacted rendering every formatter writes.</summary>
    private sealed class RedactedException(Exception original, string rendered) : Exception(Redact(original.Message))
    {
        public override string? StackTrace => original.StackTrace;

        public override string ToString() => rendered;
    }
}

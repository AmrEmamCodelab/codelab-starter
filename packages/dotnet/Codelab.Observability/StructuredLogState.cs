using System.Collections;
using System.Globalization;

namespace Codelab.Observability;

/// <summary>
/// The state of one structured log line with a constant template and named values, for a failure line whose level
/// is decided at run time and whose fields outnumber a source-generated logging method's parameter budget. Serilog
/// reads the values as properties and <see cref="OriginalFormatKey"/> as the message template, exactly as it reads a
/// <c>[LoggerMessage]</c> method's state. Use with <c>ILogger.Log(level, eventId, state, exception, Format)</c>.
/// </summary>
public sealed class StructuredLogState : IReadOnlyList<KeyValuePair<string, object?>>
{
    /// <summary>The key under which Microsoft.Extensions.Logging carries the message template.</summary>
    public const string OriginalFormatKey = "{OriginalFormat}";

    private const char PlaceholderStart = '{';
    private const char PlaceholderEnd = '}';

    private readonly string _template;
    private readonly KeyValuePair<string, object?>[] _properties;

    /// <summary>A state for <paramref name="template"/> whose placeholders are named by <paramref name="properties"/>.</summary>
    public StructuredLogState(string template, IReadOnlyList<KeyValuePair<string, object?>> properties)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        ArgumentNullException.ThrowIfNull(properties);

        _template = template;
        _properties = [.. properties];
    }

    /// <inheritdoc />
    public int Count => _properties.Length + 1;

    /// <inheritdoc />
    public KeyValuePair<string, object?> this[int index] =>
        index == _properties.Length ? KeyValuePair.Create(OriginalFormatKey, (object?)_template) : _properties[index];

    /// <summary>The formatter to pass to <c>ILogger.Log</c>: the template with each placeholder replaced by its value.</summary>
    public static string Format(StructuredLogState state, Exception? _)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.ToString();
    }

    /// <inheritdoc />
    public override string ToString() =>
        _properties.Aggregate(_template, (text, property) => text.Replace(
            PlaceholderStart + property.Key + PlaceholderEnd,
            Convert.ToString(property.Value, CultureInfo.InvariantCulture),
            StringComparison.Ordinal));

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

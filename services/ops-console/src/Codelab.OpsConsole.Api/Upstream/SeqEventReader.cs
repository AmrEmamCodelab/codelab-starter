using System.Globalization;
using System.Text.Json;
using Codelab.Observability;
using Codelab.OpsConsole.Application.ReferenceCodes;

namespace Codelab.OpsConsole.Api.Upstream;

/// <summary>
/// Reads the JSON array Seq's <c>api/events</c> answers into <see cref="LoggedEvent"/>s: <c>Timestamp</c>,
/// <c>Level</c>, <c>RenderedMessage</c>, the top-level <c>TraceId</c>, and from <c>Properties</c> (<c>[{Name, Value}]</c>)
/// the correlation id and the request line Serilog's request logging writes (<c>RequestMethod</c>, <c>RequestPath</c>,
/// <c>StatusCode</c>). An event without a parsable timestamp is skipped; missing members are left empty.
/// </summary>
public static class SeqEventReader
{
    /// <summary>Serilog request logging's method property.</summary>
    public const string RequestMethodProperty = "RequestMethod";

    /// <summary>Serilog request logging's path property.</summary>
    public const string RequestPathProperty = "RequestPath";

    /// <summary>The level Seq leaves out for Information events.</summary>
    public const string DefaultLevel = "Information";

    private const string TimestampMember = "Timestamp";
    private const string LevelMember = "Level";
    private const string RenderedMessageMember = "RenderedMessage";
    private const string TraceIdMember = "TraceId";
    private const string PropertiesMember = "Properties";
    private const string PropertyNameMember = "Name";
    private const string PropertyValueMember = "Value";

    /// <summary>The events in <paramref name="events"/> (a JSON array); anything else is an error.</summary>
    /// <exception cref="JsonException">The document is not an array.</exception>
    public static IReadOnlyList<LoggedEvent> Read(JsonElement events)
    {
        if (events.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Seq answered something other than an event array.");
        }

        return [.. events.EnumerateArray().Select(ReadOne).OfType<LoggedEvent>()];
    }

    private static LoggedEvent? ReadOne(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object || !TryTimestamp(entry, out var timestamp))
        {
            return null;
        }

        var properties = Properties(entry);
        return new LoggedEvent
        {
            Timestamp = timestamp,
            Level = Text(entry, LevelMember) ?? DefaultLevel,
            Message = Text(entry, RenderedMessageMember) ?? string.Empty,
            TraceId = Text(entry, TraceIdMember),
            CorrelationId = properties.GetValueOrDefault(LogPropertyNames.CorrelationId) is { ValueKind: JsonValueKind.String } id ? id.GetString() : null,
            Request = RequestOf(properties),
        };
    }

    private static bool TryTimestamp(JsonElement entry, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return Text(entry, TimestampMember) is { } text
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp);
    }

    private static Dictionary<string, JsonElement> Properties(JsonElement entry)
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!entry.TryGetProperty(PropertiesMember, out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return properties;
        }

        foreach (var property in list.EnumerateArray())
        {
            if (Text(property, PropertyNameMember) is { } name && property.TryGetProperty(PropertyValueMember, out var value))
            {
                properties[name] = value;
            }
        }

        return properties;
    }

    private static TracedRequest? RequestOf(Dictionary<string, JsonElement> properties)
    {
        if (properties.GetValueOrDefault(RequestMethodProperty) is not { ValueKind: JsonValueKind.String } method
            || properties.GetValueOrDefault(RequestPathProperty) is not { ValueKind: JsonValueKind.String } path
            || properties.GetValueOrDefault(LogPropertyNames.StatusCode) is not { ValueKind: JsonValueKind.Number } status
            || !status.TryGetInt32(out var statusCode))
        {
            return null;
        }

        return new TracedRequest(method.GetString()!, path.GetString()!, statusCode);
    }

    private static string? Text(JsonElement element, string member) =>
        element.TryGetProperty(member, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

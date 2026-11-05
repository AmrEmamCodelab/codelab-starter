using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Starter.DocsGen.Rendering;

/// <summary>
/// Makes the served OpenAPI document byte-stable: drops <c>servers</c> (it carries the request's host), sorts the
/// <c>paths</c> and every <c>components</c> map by key, and writes two-space indented JSON with LF line endings.
/// </summary>
internal static class OpenApiNormalizer
{
    /// <summary>The property removed from the document root.</summary>
    public const string ServersProperty = "servers";

    private const string PathsProperty = "paths";
    private const string ComponentsProperty = "components";

    /// <summary>The JSON writer options of every generated JSON file.</summary>
    public static JsonSerializerOptions WriterOptions { get; } = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = MarkdownText.NewLine.ToString(),
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The normalized document.</summary>
    /// <exception cref="InvalidOperationException">The text is not a JSON object.</exception>
    public static string Normalize(string openApiJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(openApiJson);
        if (JsonNode.Parse(openApiJson) is not JsonObject document)
        {
            throw new InvalidOperationException("The OpenAPI document is not a JSON object.");
        }

        document.Remove(ServersProperty);
        SortByKey(document, PathsProperty);
        if (document[ComponentsProperty] is JsonObject components)
        {
            foreach (var name in components.Select(entry => entry.Key).ToList())
            {
                SortByKey(components, name);
            }
        }

        return document.ToJsonString(WriterOptions) + MarkdownText.NewLine;
    }

    private static void SortByKey(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is not JsonObject map)
        {
            return;
        }

        var sorted = new JsonObject();
        foreach (var (key, value) in map.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToList())
        {
            map.Remove(key);
            sorted[key] = value;
        }

        parent[propertyName] = sorted;
    }
}

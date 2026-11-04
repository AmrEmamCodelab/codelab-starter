using System.Text.Json;
using Codelab.Testing.Checks;

namespace Codelab.Testing.Contracts;

/// <summary>
/// The value-level PublicId check: walks what an API actually sent and fails on a bare number under an id-shaped JSON
/// key (<c>id</c>, <c>…Id</c>, <c>…Ids</c>), also when the number hides in a string. It sees blind spots of the
/// type-level check (a Domain record returned directly, an id formatted into text). Allowlisted keys are lookup ids.
/// </summary>
public static class WireJsonSweep
{
    private const string IdKey = "id";
    private const string IdSuffix = "Id";
    private const string IdsSuffix = "Ids";
    private const string RootPath = "$";

    /// <summary>Every JSON path in <paramref name="json"/> where an id-shaped key carries a number.</summary>
    public static IReadOnlyList<string> FindNumericIds(string json, IReadOnlyCollection<string> allowedKeys)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(allowedKeys);
        using var document = JsonDocument.Parse(json);
        var findings = new List<string>();
        Walk(document.RootElement, RootPath, new HashSet<string>(allowedKeys, StringComparer.Ordinal), findings);
        return findings;
    }

    /// <summary>The sweep of one response body as a check; <paramref name="source"/> names it (for example the endpoint).</summary>
    public static CheckResult Check(string json, string source, IReadOnlyCollection<string> allowedKeys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var findings = FindNumericIds(json, allowedKeys);
        return new CheckResult($"No numeric id in the response of {source}", findings.Select(path => $"{path} is a bare number"), examined: 1);
    }

    /// <summary>True for <c>id</c> or a camelCase key ending in <c>Id</c> / <c>Ids</c>.</summary>
    public static bool IsIdKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return string.Equals(key, IdKey, StringComparison.OrdinalIgnoreCase)
            || key.EndsWith(IdSuffix, StringComparison.Ordinal)
            || key.EndsWith(IdsSuffix, StringComparison.Ordinal);
    }

    private static void Walk(JsonElement element, string path, HashSet<string> allowed, List<string> findings)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                Walk(item, $"{path}[{index}]", allowed, findings);
                index++;
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            var childPath = $"{path}.{property.Name}";
            if (IsIdKey(property.Name) && !allowed.Contains(property.Name))
            {
                findings.AddRange(NumericValues(property.Value, childPath));
            }

            Walk(property.Value, childPath, allowed, findings);
        }
    }

    private static IEnumerable<string> NumericValues(JsonElement value, string path) => value.ValueKind switch
    {
        JsonValueKind.Number => [path],
        JsonValueKind.String when IsDigits(value.GetString()) => [path],
        JsonValueKind.Array => value.EnumerateArray()
            .Select((item, index) => (item, index))
            .Where(entry => entry.item.ValueKind == JsonValueKind.Number || (entry.item.ValueKind == JsonValueKind.String && IsDigits(entry.item.GetString())))
            .Select(entry => $"{path}[{entry.index}]"),
        _ => [],
    };

    private static bool IsDigits(string? text) => !string.IsNullOrEmpty(text) && text.All(char.IsAsciiDigit);
}

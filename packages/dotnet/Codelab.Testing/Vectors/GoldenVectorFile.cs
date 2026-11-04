using System.Text.Json;

namespace Codelab.Testing.Vectors;

/// <summary>
/// One golden-vector file, <c>packages/vectors/&lt;area&gt;.vectors.json</c>:
/// <c>{ schemaVersion, prefix, fixtureKeyId, cases: [ { name, &lt;inputs&gt;, expected… } ] }</c>. Inputs and expected
/// values are read by field name, so the format stays owned by the vector files, not by this runner.
/// </summary>
public sealed class GoldenVectorFile
{
    /// <summary>The field-name prefix of an expected value in a case (<c>expectedCanonicalBase64</c>).</summary>
    public const string ExpectedPrefix = "expected";

    private GoldenVectorFile(int schemaVersion, string prefix, string fixtureKeyId, IReadOnlyList<GoldenVectorCase> cases)
    {
        SchemaVersion = schemaVersion;
        Prefix = prefix;
        FixtureKeyId = fixtureKeyId;
        Cases = cases;
    }

    /// <summary>The format version of the file.</summary>
    public int SchemaVersion { get; }

    /// <summary>The protocol prefix the expected values were generated with (<c>CODELABTEST</c>).</summary>
    public string Prefix { get; }

    /// <summary>The id of the fixture key the expected values were generated with.</summary>
    public string FixtureKeyId { get; }

    /// <summary>The cases, in file order.</summary>
    public IReadOnlyList<GoldenVectorCase> Cases { get; }

    /// <summary>Reads a vector file.</summary>
    public static GoldenVectorFile Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllText(path), path);
    }

    /// <summary>Parses vector JSON; <paramref name="sourceName"/> names the input in error messages.</summary>
    /// <exception cref="InvalidDataException">A required field is missing or a case has no name.</exception>
    public static GoldenVectorFile Parse(string json, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var cases = Required(root, VectorFields.Cases, JsonValueKind.Array, sourceName)
            .EnumerateArray()
            .Select((element, index) => GoldenVectorCase.From(element, $"{sourceName} case {index}"))
            .ToList();
        return new GoldenVectorFile(
            Required(root, VectorFields.SchemaVersion, JsonValueKind.Number, sourceName).GetInt32(),
            Required(root, VectorFields.Prefix, JsonValueKind.String, sourceName).GetString()!,
            Required(root, VectorFields.FixtureKeyId, JsonValueKind.String, sourceName).GetString()!,
            cases);
    }

    internal static JsonElement Required(JsonElement element, string field, JsonValueKind kind, string sourceName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(field, out var value) || value.ValueKind != kind)
        {
            throw new InvalidDataException($"{sourceName}: '{field}' is missing or not a {kind}.");
        }

        return value;
    }
}

/// <summary>One case of a golden-vector file.</summary>
public sealed class GoldenVectorCase
{
    private readonly Dictionary<string, JsonElement> _fields;

    private GoldenVectorCase(string name, Dictionary<string, JsonElement> fields)
    {
        Name = name;
        _fields = fields;
    }

    /// <summary>The case name.</summary>
    public string Name { get; }

    /// <summary>The expected values (every string field whose name starts with <c>expected</c>).</summary>
    public IReadOnlyDictionary<string, string> Expected =>
        _fields.Where(entry => entry.Key.StartsWith(GoldenVectorFile.ExpectedPrefix, StringComparison.Ordinal) && entry.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(entry => entry.Key, entry => entry.Value.GetString()!, StringComparer.Ordinal);

    /// <summary>An input field, as raw JSON (a copy that outlives the parsed document).</summary>
    /// <exception cref="KeyNotFoundException">The case has no such field.</exception>
    public JsonElement Input(string field) =>
        _fields.TryGetValue(field, out var value) ? value : throw new KeyNotFoundException($"Vector case '{Name}' has no field '{field}'.");

    /// <summary>A string input field.</summary>
    public string InputString(string field) =>
        Input(field).GetString() ?? throw new InvalidDataException($"Vector case '{Name}' field '{field}' is null.");

    internal static GoldenVectorCase From(JsonElement element, string location)
    {
        var name = GoldenVectorFile.Required(element, VectorFields.Name, JsonValueKind.String, location).GetString()!;
        var fields = element.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        return new GoldenVectorCase(name, fields);
    }
}

internal static class VectorFields
{
    public const string SchemaVersion = "schemaVersion";
    public const string Prefix = "prefix";
    public const string FixtureKeyId = "fixtureKeyId";
    public const string Cases = "cases";
    public const string Name = "name";
}

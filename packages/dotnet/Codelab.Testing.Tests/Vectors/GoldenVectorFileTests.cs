using System.Text.Json;
using Codelab.Testing.Tests.Support;
using Codelab.Testing.Vectors;

namespace Codelab.Testing.Tests.Vectors;

public sealed class GoldenVectorFileTests
{
    private const string Json = """
        {
          "schemaVersion": 1,
          "prefix": "CODELABTEST",
          "fixtureKeyId": "fixture-1",
          "cases": [
            { "name": "a", "method": "GET", "headers": { "x": "1" }, "expectedCanonicalBase64": "QQ==", "expectedCount": 3 }
          ]
        }
        """;

    [Fact]
    public void A_file_is_parsed_with_inputs_and_string_expectations()
    {
        var file = GoldenVectorFile.Parse(Json, "inline");

        Assert.Equal((1, "CODELABTEST", "fixture-1"), (file.SchemaVersion, file.Prefix, file.FixtureKeyId));
        var vectorCase = Assert.Single(file.Cases);
        Assert.Equal("a", vectorCase.Name);
        Assert.Equal("GET", vectorCase.InputString("method"));
        Assert.Equal(JsonValueKind.Object, vectorCase.Input("headers").ValueKind);
        Assert.Equal(["expectedCanonicalBase64"], vectorCase.Expected.Keys);
        Assert.Throws<KeyNotFoundException>(() => vectorCase.Input("missing"));
    }

    [Theory]
    [InlineData("""{ "prefix": "P", "fixtureKeyId": "k", "cases": [] }""")]
    [InlineData("""{ "schemaVersion": 1, "fixtureKeyId": "k", "cases": [] }""")]
    [InlineData("""{ "schemaVersion": 1, "prefix": "P", "fixtureKeyId": "k", "cases": {} }""")]
    [InlineData("""{ "schemaVersion": 1, "prefix": "P", "fixtureKeyId": "k", "cases": [ { "method": "GET" } ] }""")]
    [InlineData("""[]""")]
    public void Malformed_files_are_refused(string json) =>
        Assert.Throws<InvalidDataException>(() => GoldenVectorFile.Parse(json, "inline"));

    [Fact]
    public void A_file_loads_from_disk_and_a_null_input_is_refused()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.Write("canonical.vectors.json", Json.Replace("\"GET\"", "null", StringComparison.Ordinal));

        var file = GoldenVectorFile.Load(path);

        Assert.Throws<InvalidDataException>(() => file.Cases[0].InputString("method"));
    }
}

using System.Text.Json.Nodes;
using Codelab.Security.Canonical;
using Codelab.Testing.Vectors;

namespace Codelab.Security.Tests.Vectors;

/// <summary>
/// <c>canonical.vectors.json</c>: <c>{Prefix}-CANON-v1</c> framing of field lists. Covers empty lists and fields,
/// boundary shifts (<c>["a","bc"]</c> vs <c>["ab","c"]</c>), delimiters and NUL inside fields, Arabic, Arabic-Indic
/// digits, bidi marks, astral characters, combining marks (no normalisation), a two-digit field count and a large field.
/// </summary>
internal sealed class CanonicalVectors : IVectorArea
{
    private const string ProfileField = "profile";
    private const string FieldsField = "fields";
    private const string Profile = "VECTOR-PROFILE";
    private const int LargeFieldRepeat = 600;
    private const int ManyFields = 12;

    public string Name => "canonical";

    public string FixtureKeyId(FixtureKeySet keys) => VectorGenerator.NoFixtureKey;

    public IEnumerable<JsonObject> Inputs(VectorContext context)
    {
        yield return Frame("no-fields", Profile);
        yield return Frame("one-empty-field", Profile, string.Empty);
        yield return Frame("ascii-fields", Profile, "alpha", "beta", "gamma");
        yield return Frame("boundary-shift-left", Profile, "a", "bc");
        yield return Frame("boundary-shift-right", Profile, "ab", "c");
        yield return Frame("delimiters-inside-fields", Profile, "1:x\n", ":", "\n\n", "a\0b");
        yield return Frame("arabic-fields", Profile, "مرحبا بالعالم", "السلام عليكم");
        yield return Frame("arabic-indic-digits", Profile, "١٢٣٤٥", "۱۲۳");
        yield return Frame("bidi-marks", Profile, "abc ‏مرحبا‎ 123");
        yield return Frame("astral-characters", Profile, "😀", "𝄞 clef");
        yield return Frame("combining-marks-not-normalised", Profile, "é", "é");
        yield return Frame("arabic-profile", "PROFILE/ملف", "value");
        yield return Frame("protocol-profile", context.Protocol.RequestProfile, context.Protocol.SignatureVersion);
        yield return Frame("two-digit-field-count", Profile, [.. Enumerable.Range(1, ManyFields).Select(index => $"f{index}")]);
        yield return Frame("large-field", Profile, string.Concat(Enumerable.Repeat("ab", LargeFieldRepeat)));
    }

    public IReadOnlyDictionary<string, string> Compute(GoldenVectorCase vectorCase, VectorContext context)
    {
        var fields = vectorCase.Input(FieldsField).EnumerateArray().Select(field => field.GetString()!).ToArray();
        var bytes = new CanonicalFraming(context.Protocol).BuildUtf8(vectorCase.InputString(ProfileField), fields);
        return new Dictionary<string, string>(StringComparer.Ordinal) { [CaseFields.ExpectedCanonicalBase64] = Convert.ToBase64String(bytes) };
    }

    private static JsonObject Frame(string name, string profile, params string[] fields) => new()
    {
        [CaseFields.Name] = name,
        [CaseFields.Kind] = CaseKinds.Frame,
        [ProfileField] = profile,
        [FieldsField] = new JsonArray([.. fields.Select(field => (JsonNode)JsonValue.Create(field))]),
    };
}

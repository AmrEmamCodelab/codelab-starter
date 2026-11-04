using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Codelab.Security.Canonical;
using Codelab.Security.Signing;
using Codelab.Testing.Vectors;

namespace Codelab.Security.Tests.Vectors;

/// <summary>The field names shared by the vector files (the TypeScript runner reads the same names).</summary>
internal static class CaseFields
{
    public const string Name = "name";
    public const string Kind = "kind";
    public const string MasterKeyId = "masterKeyId";
    public const string UserId = "userId";
    public const string SessionId = "sessionId";
    public const string Body = "body";
    public const string BodyUnsigned = "bodyUnsigned";
    public const string PresentedHeader = "presentedHeader";
    public const string ExpectedBodyHash = "expectedBodyHash";
    public const string ExpectedCanonicalBase64 = "expectedCanonicalBase64";
    public const string ExpectedSignature = "expectedSignature";
    public const string ExpectedHeader = "expectedHeader";
    public const string ExpectedVerdict = "expectedVerdict";
}

/// <summary>The case kinds.</summary>
internal static class CaseKinds
{
    public const string Frame = "frame";
    public const string Derive = "derive";
    public const string Sign = "sign";
    public const string Verify = "verify";
    public const string Protect = "protect";
    public const string Open = "open";
}

/// <summary>
/// The verdict of a verify case, the same on both runners: <c>malformed</c> when the header does not parse or its MAC
/// is not the one canonical spelling of 32 bytes, <c>mismatch</c> when it parses but does not verify.
/// </summary>
internal static class Verdicts
{
    public const string Valid = "valid";
    public const string Malformed = "malformed";
    public const string Mismatch = "mismatch";
}

/// <summary>
/// A body in a vector file: <c>{ "encoding": "utf8", "text" }</c>, <c>{ "encoding": "base64", "base64" }</c> or
/// <c>{ "encoding": "repeat", "text", "count" }</c> (the UTF-8 text repeated: large bodies without large files).
/// </summary>
internal static class VectorBody
{
    private const string EncodingField = "encoding";
    private const string TextField = "text";
    private const string Base64Field = "base64";
    private const string CountField = "count";
    private const string Utf8Encoding = "utf8";
    private const string Base64Encoding = "base64";
    private const string RepeatEncoding = "repeat";

    public static JsonObject Utf8(string text) => new() { [EncodingField] = Utf8Encoding, [TextField] = text };

    public static JsonObject Binary(byte[] bytes) => new() { [EncodingField] = Base64Encoding, [Base64Field] = Convert.ToBase64String(bytes) };

    public static JsonObject Repeat(string text, int count) => new() { [EncodingField] = RepeatEncoding, [TextField] = text, [CountField] = count };

    public static JsonObject Empty() => Utf8(string.Empty);

    /// <summary>The body bytes a case describes.</summary>
    public static byte[] Decode(JsonElement body)
    {
        var encoding = body.GetProperty(EncodingField).GetString();
        return encoding switch
        {
            Utf8Encoding => Encoding.UTF8.GetBytes(body.GetProperty(TextField).GetString()!),
            Base64Encoding => Convert.FromBase64String(body.GetProperty(Base64Field).GetString()!),
            RepeatEncoding => Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(body.GetProperty(TextField).GetString()!, body.GetProperty(CountField).GetInt32()))),
            _ => throw new InvalidDataException($"Unknown body encoding '{encoding}'."),
        };
    }
}

/// <summary>Reading helpers over a parsed case.</summary>
internal static class CaseReading
{
    public static string Kind(this GoldenVectorCase vectorCase) => vectorCase.InputString(CaseFields.Kind);

    public static byte[] BodyBytes(this GoldenVectorCase vectorCase) => VectorBody.Decode(vectorCase.Input(CaseFields.Body));

    public static bool Flag(this GoldenVectorCase vectorCase, string field) => vectorCase.Input(field).GetBoolean();

    /// <summary>The body-hash field: the marker for an exempted body, else the digest of the bytes.</summary>
    public static string BodyHashField(this GoldenVectorCase vectorCase) =>
        vectorCase.Flag(CaseFields.BodyUnsigned) ? SignatureCanonicalForm.UnsignedBody : SignatureCanonicalForm.HashBody(vectorCase.BodyBytes());

    /// <summary>The per-session key the case's master key, user and session derive.</summary>
    public static byte[] SessionKey(this GoldenVectorCase vectorCase, VectorContext context) =>
        context.SessionKey(
            masterKeyId: vectorCase.InputString(CaseFields.MasterKeyId),
            new SessionIdentity(vectorCase.InputString(CaseFields.UserId), vectorCase.InputString(CaseFields.SessionId)));
}

/// <summary>Deterministic values derived from a case name (nonces), so no random value is committed by hand.</summary>
internal static class Deterministic
{
    private const int NonceBytes = 16;

    /// <summary>A 22-character base64url nonce derived from <paramref name="seed"/>.</summary>
    public static string Nonce(string seed) => Base64UrlText.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(seed)).AsSpan(0, NonceBytes));
}

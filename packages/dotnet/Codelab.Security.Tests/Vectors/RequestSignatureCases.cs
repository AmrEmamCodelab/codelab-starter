using System.Text.Json.Nodes;

namespace Codelab.Security.Tests.Vectors;

/// <summary>
/// The request-signature case catalogue. Sign cases cover empty, JSON, Arabic, binary, large and exempted bodies,
/// unsorted and repeated query parameters, percent-escape normalisation, a lower-case method and both master keys.
/// Verify cases sign one baseline request, then present it with exactly one thing changed.
/// </summary>
internal static class RequestSignatureCases
{
    private const string ArabicJson = "{\"title\":\"طلب خدمة جديد\",\"note\":\"مرحبا بالعالم\"}";
    private const string ArabicJsonTampered = "{\"title\":\"طلب خدمة جديدة\",\"note\":\"مرحبا بالعالم\"}";
    private const string LargeBodyLine = "Widget order line ١٢٣ مرحبا\n";
    private const int LargeBodyRepeat = 4096;
    private const int ByteValues = 256;

    public static IEnumerable<JsonObject> All(VectorContext context) =>
        SignCases(context.Keys).Select(input => input.ToJson(CaseKinds.Sign)).Concat(VerifyCases(context));

    private static IEnumerable<RequestInput> SignCases(FixtureKeySet keys)
    {
        var active = keys.Signing.ActiveKeyId;
        var retired = keys.Signing.Materials.Keys.First(keyId => keyId != active);
        yield return new() { Name = "get-no-query-empty-body", MasterKeyId = active };
        yield return new() { Name = "get-query-unsorted-repeated-empty-values", MasterKeyId = active, Query = "z=1&a=2&a=1&empty=&flag" };
        yield return new() { Name = "get-query-percent-encoded-arabic", MasterKeyId = active, Query = "q=%D9%85%D8%B1%D8%AD%D8%A8%D8%A7&page=2" };
        yield return new() { Name = "get-query-plus-and-encoded-space", MasterKeyId = active, Query = "name=a+b&note=%20x" };
        yield return new() { Name = "post-json-ascii", MasterKeyId = active, Method = "POST", Body = VectorBody.Utf8("{\"title\":\"Widget\",\"count\":3}") };
        yield return new() { Name = "post-json-arabic", MasterKeyId = active, Method = "POST", Body = VectorBody.Utf8(ArabicJson) };
        yield return new() { Name = "put-binary-body", MasterKeyId = active, Method = "PUT", Path = "/records/blob", Body = VectorBody.Binary([.. Enumerable.Range(0, ByteValues).Select(value => (byte)value)]) };
        yield return new() { Name = "patch-lower-case-method", MasterKeyId = active, Method = "patch", Body = VectorBody.Utf8("{\"state\":2}") };
        yield return new() { Name = "path-escapes-normalised", MasterKeyId = active, Path = "/records/%7euser/%2fa/%c3%a9" };
        yield return new() { Name = "path-percent-encoded-arabic", MasterKeyId = active, Path = "/records/%D8%B7%D9%84%D8%A8" };
        yield return new() { Name = "post-large-body", MasterKeyId = active, Method = "POST", Body = VectorBody.Repeat(LargeBodyLine, LargeBodyRepeat) };
        yield return new() { Name = "delete-no-body", MasterKeyId = active, Method = "DELETE", Path = "/records/widget-9" };
        yield return new() { Name = "post-exempted-multipart-body", MasterKeyId = active, Method = "POST", Path = "/uploads", BodyUnsigned = true };
        yield return new() { Name = "timestamp-zero", MasterKeyId = active, Timestamp = "0" };
        yield return new() { Name = "timestamp-largest", MasterKeyId = active, Timestamp = "9999999999" };
        yield return new() { Name = "arabic-session-identity", MasterKeyId = active, UserId = "مستخدم", SessionId = "جلسة-٧" };
        yield return new() { Name = "retired-master-key", MasterKeyId = retired, Method = "POST", Body = VectorBody.Utf8("{}") };
    }

    private static IEnumerable<JsonObject> VerifyCases(VectorContext context)
    {
        var active = context.Keys.Signing.ActiveKeyId;
        var signed = new RequestInput { Name = "baseline", MasterKeyId = active, Method = "POST", Path = "/records/%D8%B7", Query = "b=2&a=1", Body = VectorBody.Utf8(ArabicJson) };
        var header = RequestSignatureVectors.Sign(signed, context).Header;
        yield return Present("untampered", signed, header);
        yield return Present("path-escape-case-differs", signed with { Path = "/records/%d8%b7" }, header);
        yield return Present("method-spelled-lower-case", signed with { Method = "post" }, header);
        yield return Present("body-tampered", signed with { Body = VectorBody.Utf8(ArabicJsonTampered) }, header);
        yield return Present("body-emptied", signed with { Body = VectorBody.Empty() }, header);
        yield return Present("path-tampered", signed with { Path = "/records/%D8%B8" }, header);
        yield return Present("query-reordered", signed with { Query = "a=1&b=2" }, header);
        yield return Present("query-extended", signed with { Query = "b=2&a=1&admin=1" }, header);
        yield return Present("audience-other", signed with { Audience = "Starter.Other" }, header);
        yield return Present("method-changed", signed with { Method = "PUT" }, header);
        yield return Present("user-other", signed with { UserId = "8" }, header);
        yield return Present("session-other", signed with { SessionId = "session-b" }, header);
        yield return Present("body-claimed-exempt", signed with { BodyUnsigned = true }, header);
        foreach (var edit in HeaderEdits.All)
        {
            yield return Present(edit.Name, signed, edit.Apply(header));
        }

        yield return Present("signed-under-other-prefix", signed, RequestSignatureVectors.Sign(signed, context with { Protocol = HeaderEdits.ForeignProtocol(context.Protocol) }).Header);
        var retired = context.Keys.Signing.Materials.Keys.First(keyId => keyId != active);
        var retiredSigned = signed with { MasterKeyId = retired };
        var retiredHeader = RequestSignatureVectors.Sign(retiredSigned, context).Header;
        yield return Present("retired-master-key-session", retiredSigned, retiredHeader);
        yield return Present("retired-key-proof-under-active-key", signed, retiredHeader);
    }

    private static JsonObject Present(string name, RequestInput presented, string header)
    {
        var json = (presented with { Name = name }).ToJson(CaseKinds.Verify);
        json[CaseFields.PresentedHeader] = header;
        return json;
    }
}

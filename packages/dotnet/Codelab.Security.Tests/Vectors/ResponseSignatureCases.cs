using System.Text.Json.Nodes;

namespace Codelab.Security.Tests.Vectors;

/// <summary>
/// The response-signature case catalogue. Sign cases cover JSON (ASCII and Arabic), created, empty, problem+json,
/// binary, large, exempted (file download) and content-type-less responses. Verify cases sign one baseline response,
/// then present it with one thing changed.
/// </summary>
internal static class ResponseSignatureCases
{
    private const string ArabicJson = "{\"status\":\"تمت الموافقة\",\"items\":[\"أ\",\"ب\"]}";
    private const string ProblemJson = "{\"code\":\"FORBIDDEN\",\"correlationId\":\"c-1\",\"referenceCode\":\"REF-7\"}";
    private const string ProblemContentType = "application/problem+json";
    private const string BinaryContentType = "application/octet-stream";
    private const string LargeBodyLine = "{\"line\":\"Widget ١٢٣ مرحبا\"},\n";
    private const int LargeBodyRepeat = 4096;
    private const int ByteValues = 256;

    public static IEnumerable<JsonObject> All(VectorContext context) =>
        SignCases(context.Keys).Select(input => input.ToJson(CaseKinds.Sign)).Concat(VerifyCases(context));

    private static IEnumerable<ResponseInput> SignCases(FixtureKeySet keys)
    {
        var active = keys.Signing.ActiveKeyId;
        var retired = keys.Signing.Materials.Keys.First(keyId => keyId != active);
        yield return new() { Name = "ok-json-ascii", MasterKeyId = active, Body = VectorBody.Utf8("{\"id\":\"m1_x\",\"count\":3}") };
        yield return new() { Name = "ok-json-arabic", MasterKeyId = active, Body = VectorBody.Utf8(ArabicJson) };
        yield return new() { Name = "created", MasterKeyId = active, Status = StatusCodes.Status201Created, Body = VectorBody.Utf8("{}") };
        yield return new() { Name = "no-content-empty-body", MasterKeyId = active, Status = StatusCodes.Status204NoContent, ContentType = string.Empty };
        yield return new() { Name = "forbidden-problem-json", MasterKeyId = active, Status = StatusCodes.Status403Forbidden, ContentType = ProblemContentType, Body = VectorBody.Utf8(ProblemJson) };
        yield return new() { Name = "ok-binary", MasterKeyId = active, ContentType = BinaryContentType, Body = VectorBody.Binary([.. Enumerable.Range(0, ByteValues).Select(value => (byte)(ByteValues - 1 - value))]) };
        yield return new() { Name = "ok-large-body", MasterKeyId = active, Body = VectorBody.Repeat(LargeBodyLine, LargeBodyRepeat) };
        yield return new() { Name = "file-download-exempted-body", MasterKeyId = active, ContentType = BinaryContentType, Body = VectorBody.Utf8("file bytes"), BodyUnsigned = true };
        yield return new() { Name = "retired-master-key", MasterKeyId = retired, Body = VectorBody.Utf8("{}") };
    }

    private static IEnumerable<JsonObject> VerifyCases(VectorContext context)
    {
        var active = context.Keys.Signing.ActiveKeyId;
        var signed = new ResponseInput { Name = "baseline", MasterKeyId = active, Body = VectorBody.Utf8(ArabicJson) };
        var header = ResponseSignatureVectors.Sign(signed, context).Header;
        yield return Present("untampered", signed, header);
        yield return Present("status-swapped", signed with { Status = StatusCodes.Status403Forbidden }, header);
        yield return Present("body-tampered", signed with { Body = VectorBody.Utf8(ArabicJson.Replace('ب', 'ت')) }, header);
        yield return Present("content-type-changed", signed with { ContentType = "text/html" }, header);
        yield return Present("other-request-nonce", signed with { RequestNonce = Deterministic.Nonce("another request") }, header);
        yield return Present("session-other", signed with { SessionId = "session-b" }, header);
        foreach (var edit in HeaderEdits.Common)
        {
            yield return Present(edit.Name, signed, edit.Apply(header));
        }

        yield return Present("signed-under-other-prefix", signed, ResponseSignatureVectors.Sign(signed, context with { Protocol = HeaderEdits.ForeignProtocol(context.Protocol) }).Header);
        var download = signed with { ContentType = "application/pdf", Body = VectorBody.Utf8("file bytes"), BodyUnsigned = true };
        var downloadHeader = ResponseSignatureVectors.Sign(download, context).Header;
        yield return Present("exempted-download-any-body", download with { Body = VectorBody.Utf8("other bytes") }, downloadHeader);
        yield return Present("exempted-download-other-status", download with { Status = StatusCodes.Status202Accepted }, downloadHeader);
    }

    private static JsonObject Present(string name, ResponseInput presented, string header)
    {
        var json = (presented with { Name = name, RequestNonce = presented.EffectiveRequestNonce }).ToJson(CaseKinds.Verify);
        json[CaseFields.PresentedHeader] = header;
        return json;
    }
}

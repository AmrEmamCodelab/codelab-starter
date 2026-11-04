using System.Text.Json;
using System.Text.Json.Nodes;
using Codelab.Security.Canonical;
using Codelab.Security.Signing;
using Codelab.Testing.Vectors;

namespace Codelab.Security.Tests.Vectors;

/// <summary>The inputs of one response signature.</summary>
internal sealed record ResponseInput
{
    public const string RequestNonceField = "requestNonce";
    public const string StatusField = "status";
    public const string ContentTypeField = "contentType";

    public required string Name { get; init; }

    public required string MasterKeyId { get; init; }

    public string UserId { get; init; } = "7";

    public string SessionId { get; init; } = "session-a";

    public string? RequestNonce { get; init; }

    public int Status { get; init; } = StatusCodes.Status200OK;

    public string ContentType { get; init; } = "application/json; charset=utf-8";

    public JsonObject Body { get; init; } = VectorBody.Empty();

    public bool BodyUnsigned { get; init; }

    /// <summary>The request nonce: explicit, or derived from the case name.</summary>
    public string EffectiveRequestNonce => RequestNonce ?? Deterministic.Nonce(Name);

    public static ResponseInput From(GoldenVectorCase vectorCase) => new()
    {
        Name = vectorCase.Name,
        MasterKeyId = vectorCase.InputString(CaseFields.MasterKeyId),
        UserId = vectorCase.InputString(CaseFields.UserId),
        SessionId = vectorCase.InputString(CaseFields.SessionId),
        RequestNonce = vectorCase.InputString(RequestNonceField),
        Status = vectorCase.Input(StatusField).GetInt32(),
        ContentType = vectorCase.InputString(ContentTypeField),
        Body = JsonNode.Parse(vectorCase.Input(CaseFields.Body).GetRawText())!.AsObject(),
        BodyUnsigned = vectorCase.Flag(CaseFields.BodyUnsigned),
    };

    public JsonObject ToJson(string kind) => new()
    {
        [CaseFields.Name] = Name,
        [CaseFields.Kind] = kind,
        [CaseFields.MasterKeyId] = MasterKeyId,
        [CaseFields.UserId] = UserId,
        [CaseFields.SessionId] = SessionId,
        [RequestNonceField] = EffectiveRequestNonce,
        [StatusField] = Status,
        [ContentTypeField] = ContentType,
        [CaseFields.Body] = Body.DeepClone(),
        [CaseFields.BodyUnsigned] = BodyUnsigned,
    };
}

/// <summary>A computed response signature.</summary>
internal sealed record ResponseProof(string BodyHash, byte[] Canonical, string Mac, string Header);

/// <summary>
/// <c>response-signature.vectors.json</c>: response signatures (<c>sign</c>) and presented signatures with their
/// verdict (<c>verify</c>). A verify case is judged the way the browser judges it: valid when the MAC verifies over the
/// received body's hash or over the exemption marker (a whole-file download), because the marker sits inside the MAC.
/// </summary>
internal sealed class ResponseSignatureVectors : IVectorArea
{
    public string Name => "response-signature";

    public string FixtureKeyId(FixtureKeySet keys) => keys.Signing.ActiveKeyId;

    public IEnumerable<JsonObject> Inputs(VectorContext context) => ResponseSignatureCases.All(context);

    public IReadOnlyDictionary<string, string> Compute(GoldenVectorCase vectorCase, VectorContext context) =>
        vectorCase.Kind() == CaseKinds.Sign ? ComputeSign(vectorCase, context) : ComputeVerify(vectorCase, context);

    /// <summary>Signs <paramref name="input"/> exactly as the server's response signer does.</summary>
    public static ResponseProof Sign(ResponseInput input, VectorContext context)
    {
        var bodyHash = input.BodyUnsigned ? SignatureCanonicalForm.UnsignedBody : SignatureCanonicalForm.HashBody(VectorBody.Decode(JsonSerializer.SerializeToElement(input.Body)));
        var canonical = Canonical(input, bodyHash, context.Protocol);
        var key = context.SessionKey(input.MasterKeyId, new SessionIdentity(input.UserId, input.SessionId));
        var mac = HmacSignature.Compute(key, canonical);
        return new ResponseProof(bodyHash, canonical, mac, ResponseSignatureHeader.Format(mac));
    }

    private static Dictionary<string, string> ComputeSign(GoldenVectorCase vectorCase, VectorContext context)
    {
        var proof = Sign(ResponseInput.From(vectorCase), context);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CaseFields.ExpectedBodyHash] = proof.BodyHash,
            [CaseFields.ExpectedCanonicalBase64] = Convert.ToBase64String(proof.Canonical),
            [CaseFields.ExpectedSignature] = proof.Mac,
            [CaseFields.ExpectedHeader] = proof.Header,
        };
    }

    private static Dictionary<string, string> ComputeVerify(GoldenVectorCase vectorCase, VectorContext context) =>
        new(StringComparer.Ordinal) { [CaseFields.ExpectedVerdict] = Verdict(vectorCase, context) };

    private static string Verdict(GoldenVectorCase vectorCase, VectorContext context)
    {
        if (!ResponseSignatureHeader.TryParse(vectorCase.InputString(CaseFields.PresentedHeader), out var mac)
            || !Base64UrlText.TryDecode(mac, out _))
        {
            return Verdicts.Malformed;
        }

        var input = ResponseInput.From(vectorCase);
        var key = vectorCase.SessionKey(context);
        var bodyHash = SignatureCanonicalForm.HashBody(vectorCase.BodyBytes());
        var verifies = HmacSignature.Verify(key, Canonical(input, bodyHash, context.Protocol), mac)
            || HmacSignature.Verify(key, Canonical(input, SignatureCanonicalForm.UnsignedBody, context.Protocol), mac);
        return verifies ? Verdicts.Valid : Verdicts.Mismatch;
    }

    private static byte[] Canonical(ResponseInput input, string bodyHash, SecurityProtocol protocol) =>
        new SignatureCanonicalForm(protocol).ResponseBytes(new SignedResponseFields(
            RequestNonce: input.EffectiveRequestNonce,
            Status: input.Status,
            ContentType: input.ContentType,
            BodyHash: bodyHash));
}

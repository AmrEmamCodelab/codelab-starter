using System.Text.Json;
using System.Text.Json.Nodes;
using Codelab.Security.Canonical;
using Codelab.Security.Signing;
using Codelab.Testing.Vectors;

namespace Codelab.Security.Tests.Vectors;

/// <summary>The inputs of one request proof (a sign case, or what a verify case presents).</summary>
internal sealed record RequestInput
{
    public const string AudienceField = "audience";
    public const string MethodField = "method";
    public const string PathField = "path";
    public const string QueryField = "query";
    public const string TimestampField = "timestamp";
    public const string NonceField = "nonce";

    public required string Name { get; init; }

    public required string MasterKeyId { get; init; }

    public string UserId { get; init; } = "7";

    public string SessionId { get; init; } = "session-a";

    public string Audience { get; init; } = "Starter.Api";

    public string Method { get; init; } = "GET";

    public string Path { get; init; } = "/records";

    public string Query { get; init; } = string.Empty;

    public JsonObject Body { get; init; } = VectorBody.Empty();

    public bool BodyUnsigned { get; init; }

    // Nine digits: a ten-digit value starting 1 or 2 reads as a national id to the scrub sweep. Freshness is the
    // middleware's job, not the vectors'.
    public string Timestamp { get; init; } = "987654321";

    public string? Nonce { get; init; }

    /// <summary>The nonce: explicit, or derived from the case name.</summary>
    public string EffectiveNonce => Nonce ?? Deterministic.Nonce(Name);

    /// <summary>The request inputs of a parsed case; a verify case takes timestamp and nonce from its header.</summary>
    public static RequestInput From(GoldenVectorCase vectorCase, string timestamp, string nonce) => new()
    {
        Name = vectorCase.Name,
        MasterKeyId = vectorCase.InputString(CaseFields.MasterKeyId),
        UserId = vectorCase.InputString(CaseFields.UserId),
        SessionId = vectorCase.InputString(CaseFields.SessionId),
        Audience = vectorCase.InputString(AudienceField),
        Method = vectorCase.InputString(MethodField),
        Path = vectorCase.InputString(PathField),
        Query = vectorCase.InputString(QueryField),
        Body = JsonNode.Parse(vectorCase.Input(CaseFields.Body).GetRawText())!.AsObject(),
        BodyUnsigned = vectorCase.Flag(CaseFields.BodyUnsigned),
        Timestamp = timestamp,
        Nonce = nonce,
    };

    /// <summary>The case JSON; a sign case carries timestamp and nonce, a verify case the presented header.</summary>
    public JsonObject ToJson(string kind)
    {
        var json = new JsonObject
        {
            [CaseFields.Name] = Name,
            [CaseFields.Kind] = kind,
            [CaseFields.MasterKeyId] = MasterKeyId,
            [CaseFields.UserId] = UserId,
            [CaseFields.SessionId] = SessionId,
            [AudienceField] = Audience,
            [MethodField] = Method,
            [PathField] = Path,
            [QueryField] = Query,
            [CaseFields.Body] = Body.DeepClone(),
            [CaseFields.BodyUnsigned] = BodyUnsigned,
        };
        if (kind == CaseKinds.Sign)
        {
            json[TimestampField] = Timestamp;
            json[NonceField] = EffectiveNonce;
        }

        return json;
    }
}

/// <summary>A computed request proof.</summary>
internal sealed record RequestProof(string BodyHash, byte[] Canonical, string Mac, string Header);

/// <summary>
/// <c>request-signature.vectors.json</c>: request proofs (<c>sign</c>) and presented proofs with their verdict
/// (<c>verify</c>, tamper cases). The session key is derived from the fixture master key, as the server does.
/// </summary>
internal sealed class RequestSignatureVectors : IVectorArea
{
    public string Name => "request-signature";

    public string FixtureKeyId(FixtureKeySet keys) => keys.Signing.ActiveKeyId;

    public IEnumerable<JsonObject> Inputs(VectorContext context) => RequestSignatureCases.All(context);

    public IReadOnlyDictionary<string, string> Compute(GoldenVectorCase vectorCase, VectorContext context) =>
        vectorCase.Kind() == CaseKinds.Sign ? ComputeSign(vectorCase, context) : ComputeVerify(vectorCase, context);

    /// <summary>Signs <paramref name="input"/> exactly as a client holding the derived session key would.</summary>
    public static RequestProof Sign(RequestInput input, VectorContext context)
    {
        var bodyHash = input.BodyUnsigned ? SignatureCanonicalForm.UnsignedBody : SignatureCanonicalForm.HashBody(VectorBody.Decode(JsonSerializer.SerializeToElement(input.Body)));
        var canonical = new SignatureCanonicalForm(context.Protocol).RequestBytes(Fields(input, bodyHash));
        var key = context.SessionKey(input.MasterKeyId, new SessionIdentity(input.UserId, input.SessionId));
        var mac = HmacSignature.Compute(key, canonical);
        if (!SignatureHeader.TryCreate(input.Timestamp, input.EffectiveNonce, mac, out var header))
        {
            throw new InvalidOperationException($"Vector case '{input.Name}' has a timestamp or nonce outside the header grammar.");
        }

        return new RequestProof(bodyHash, canonical, mac, header.Format());
    }

    private static Dictionary<string, string> ComputeSign(GoldenVectorCase vectorCase, VectorContext context)
    {
        var input = RequestInput.From(vectorCase, vectorCase.InputString(RequestInput.TimestampField), vectorCase.InputString(RequestInput.NonceField));
        var proof = Sign(input, context);
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
        if (!SignatureHeader.TryParse(vectorCase.InputString(CaseFields.PresentedHeader), out var header)
            || !Base64UrlText.TryDecode(header.Mac, out _))
        {
            return Verdicts.Malformed;
        }

        var input = RequestInput.From(vectorCase, header.Timestamp, header.Nonce);
        var canonical = new SignatureCanonicalForm(context.Protocol).RequestBytes(Fields(input, vectorCase.BodyHashField()));
        return HmacSignature.Verify(vectorCase.SessionKey(context), canonical, header.Mac) ? Verdicts.Valid : Verdicts.Mismatch;
    }

    private static SignedRequestFields Fields(RequestInput input, string bodyHash) =>
        new(
            Audience: input.Audience,
            Method: input.Method,
            Path: input.Path,
            Query: input.Query,
            BodyHash: bodyHash,
            Timestamp: input.Timestamp,
            Nonce: input.EffectiveNonce);
}

using System.Globalization;
using System.Text.Json.Nodes;
using Codelab.Security.Canonical;
using Codelab.Security.PublicIds;
using Codelab.Testing.Vectors;

namespace Codelab.Security.Tests.Vectors;

/// <summary>
/// <c>public-id.vectors.json</c>: <c>{Prefix}-PUBLICID-v1</c> tokens. <c>protect</c> cases pin the AAD, both HKDF info
/// strings and the deterministic token (key id, domain and id, up to <see cref="long.MaxValue"/>, so ids are strings:
/// JSON numbers lose precision above 2^53 in JavaScript). <c>open</c> cases present a token under the fixture ring
/// and pin the outcome: the id, or a refusal.
/// </summary>
internal sealed class PublicIdVectors : IVectorArea
{
    public const string KeyIdField = "keyId";
    public const string DomainField = "domain";
    public const string IdField = "id";
    public const string TokenField = "token";
    public const string ExpectedAad = "expectedAadCanonicalBase64";
    public const string ExpectedEncryptionInfo = "expectedEncryptionKdfInfoCanonicalBase64";
    public const string ExpectedMacInfo = "expectedMacKdfInfoCanonicalBase64";
    public const string ExpectedToken = "expectedToken";
    public const string ExpectedOutcome = "expectedOutcome";
    public const string ExpectedId = "expectedId";
    public const string Accepted = "accepted";
    public const string Refused = "refused";

    private const string EncryptionPurpose = "enc";
    private const string MacPurpose = "mac";

    public string Name => "public-id";

    public string FixtureKeyId(FixtureKeySet keys) => keys.PublicIds.ActiveKeyId;

    public IEnumerable<JsonObject> Inputs(VectorContext context) => PublicIdCases.All(context);

    public IReadOnlyDictionary<string, string> Compute(GoldenVectorCase vectorCase, VectorContext context) =>
        vectorCase.Kind() == CaseKinds.Protect ? ComputeProtect(vectorCase, context) : ComputeOpen(vectorCase, context);

    /// <summary>The token for (<paramref name="keyId"/>, <paramref name="domain"/>, <paramref name="id"/>).</summary>
    public static string Protect(VectorContext context, string keyId, string domain, long id) =>
        Codec(context, activeKeyId: keyId).Protect(domain, id);

    private static PublicIdCodec Codec(VectorContext context, string? activeKeyId) =>
        new(PublicIdKeyring.Create(context.Keys.PublicIds.ToOptions(activeKeyId), context.Protocol), context.Protocol);

    private static Dictionary<string, string> ComputeProtect(GoldenVectorCase vectorCase, VectorContext context)
    {
        var keyId = vectorCase.InputString(KeyIdField);
        var domain = vectorCase.InputString(DomainField);
        var id = long.Parse(vectorCase.InputString(IdField), NumberStyles.None, CultureInfo.InvariantCulture);
        var framing = new CanonicalFraming(context.Protocol);
        var spec = context.Protocol.PublicIdSpec;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ExpectedAad] = Convert.ToBase64String(framing.BuildUtf8(PublicIdCodec.AadProfile, spec, keyId, domain)),
            [ExpectedEncryptionInfo] = Convert.ToBase64String(framing.BuildUtf8(PublicIdKeyring.KdfProfile, spec, keyId, EncryptionPurpose)),
            [ExpectedMacInfo] = Convert.ToBase64String(framing.BuildUtf8(PublicIdKeyring.KdfProfile, spec, keyId, MacPurpose)),
            [ExpectedToken] = Protect(context, keyId, domain, id),
        };
    }

    private static Dictionary<string, string> ComputeOpen(GoldenVectorCase vectorCase, VectorContext context)
    {
        var accepted = Codec(context, activeKeyId: null).TryUnprotect(vectorCase.InputString(DomainField), vectorCase.InputString(TokenField), out var id);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ExpectedOutcome] = accepted ? Accepted : Refused,
            [ExpectedId] = accepted ? id.ToString(CultureInfo.InvariantCulture) : string.Empty,
        };
    }
}

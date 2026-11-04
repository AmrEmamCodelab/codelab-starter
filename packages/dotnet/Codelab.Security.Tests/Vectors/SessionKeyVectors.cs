using System.Text.Json.Nodes;
using Codelab.Security.Canonical;
using Codelab.Testing.Vectors;

namespace Codelab.Security.Tests.Vectors;

/// <summary>
/// <c>session-keys.vectors.json</c>: the per-session signing key (HKDF-SHA256, no salt, info =
/// <c>{Prefix}-CANON-v1</c> framing of <c>[{Prefix}-SIG-v1, sessionId, userId]</c> under <c>{Prefix}-SIG-v1/KDF</c>).
/// Covers both master keys, swapped identities, Arabic and delimiter-bearing ids and long ids.
/// </summary>
internal sealed class SessionKeyVectors : IVectorArea
{
    public const string ExpectedKdfInfo = "expectedKdfInfoCanonicalBase64";
    public const string ExpectedSessionKey = "expectedSessionKeyBase64";

    private const int LongIdLength = 200;

    public string Name => "session-keys";

    public string FixtureKeyId(FixtureKeySet keys) => keys.Signing.ActiveKeyId;

    public IEnumerable<JsonObject> Inputs(VectorContext context)
    {
        var active = context.Keys.Signing.ActiveKeyId;
        var retired = context.Keys.Signing.Materials.Keys.First(keyId => keyId != active);
        yield return Derive("ascii-ids", masterKeyId: active, userId: "7", sessionId: "session-a");
        yield return Derive("swapped-ids-differ", masterKeyId: active, userId: "session-a", sessionId: "7");
        yield return Derive("guid-session", masterKeyId: active, userId: "1024", sessionId: "3f2c8a1e-5b7d-4c9e-8a6f-0d1e2f3a4b5c");
        yield return Derive("arabic-user-id", masterKeyId: active, userId: "مستخدم-٤٢", sessionId: "جلسة");
        yield return Derive("delimiters-in-ids", masterKeyId: active, userId: "a:b\nc", sessionId: "1:\n");
        yield return Derive("long-ids", masterKeyId: active, userId: new string('u', LongIdLength), sessionId: new string('s', LongIdLength));
        yield return Derive("retired-master-key", masterKeyId: retired, userId: "7", sessionId: "session-a");
    }

    public IReadOnlyDictionary<string, string> Compute(GoldenVectorCase vectorCase, VectorContext context)
    {
        var protocol = context.Protocol;
        var info = new CanonicalFraming(protocol).BuildUtf8(
            protocol.SessionKeyProfile,
            protocol.SignatureVersion,
            vectorCase.InputString(CaseFields.SessionId),
            vectorCase.InputString(CaseFields.UserId));
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ExpectedKdfInfo] = Convert.ToBase64String(info),
            [ExpectedSessionKey] = Convert.ToBase64String(vectorCase.SessionKey(context)),
        };
    }

    private static JsonObject Derive(string name, string masterKeyId, string userId, string sessionId) => new()
    {
        [CaseFields.Name] = name,
        [CaseFields.Kind] = CaseKinds.Derive,
        [CaseFields.MasterKeyId] = masterKeyId,
        [CaseFields.UserId] = userId,
        [CaseFields.SessionId] = sessionId,
    };
}

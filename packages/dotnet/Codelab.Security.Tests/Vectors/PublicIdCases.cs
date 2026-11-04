using System.Globalization;
using System.Text.Json.Nodes;
using Codelab.Security.PublicIds;

namespace Codelab.Security.Tests.Vectors;

/// <summary>
/// The PublicId case catalogue: ids from 1 to <see cref="long.MaxValue"/> (past the JavaScript safe-integer range),
/// the shortest and longest domain label, both ring keys; and open cases for a valid token, a retired key, another
/// family, a flipped payload character, an unknown or upper-case key id, a truncated token and another prefix.
/// </summary>
internal static class PublicIdCases
{
    private const string Record = "record";
    private const string User = "user";
    private const string ShortestDomain = "a";
    private const string LongestDomain = "widget-family-0123456789-abcdefg";
    private const long TypicalId = 42;
    private const long PastInt32 = 4_294_967_296;
    private const long PastJavaScriptSafeInteger = 9_007_199_254_740_993;
    private const string UnknownKeyId = "zz9";
    private const int FlippedPayloadIndex = 24;

    public static IEnumerable<JsonObject> All(VectorContext context) => ProtectCases(context).Concat(OpenCases(context));

    private static IEnumerable<JsonObject> ProtectCases(VectorContext context)
    {
        var active = context.Keys.PublicIds.ActiveKeyId;
        var retired = context.Keys.PublicIds.Materials.Keys.First(keyId => keyId != active);
        yield return Protect("smallest-id", keyId: active, domain: Record, id: PublicIdFormat.MinimumId);
        yield return Protect("typical-id", keyId: active, domain: Record, id: TypicalId);
        yield return Protect("same-id-other-family", keyId: active, domain: User, id: TypicalId);
        yield return Protect("id-past-int32", keyId: active, domain: Record, id: PastInt32);
        yield return Protect("id-past-javascript-safe-integer", keyId: active, domain: Record, id: PastJavaScriptSafeInteger);
        yield return Protect("largest-id", keyId: active, domain: Record, id: long.MaxValue);
        yield return Protect("shortest-domain", keyId: active, domain: ShortestDomain, id: TypicalId);
        yield return Protect("longest-domain", keyId: active, domain: LongestDomain, id: TypicalId);
        yield return Protect("retired-key", keyId: retired, domain: Record, id: TypicalId);
    }

    private static IEnumerable<JsonObject> OpenCases(VectorContext context)
    {
        var active = context.Keys.PublicIds.ActiveKeyId;
        var retired = context.Keys.PublicIds.Materials.Keys.First(keyId => keyId != active);
        var token = PublicIdVectors.Protect(context, keyId: active, domain: Record, id: TypicalId);
        yield return Open("valid-token", domain: Record, token: token);
        yield return Open("largest-id-token", domain: Record, token: PublicIdVectors.Protect(context, keyId: active, domain: Record, id: long.MaxValue));
        yield return Open("retired-key-token", domain: Record, token: PublicIdVectors.Protect(context, keyId: retired, domain: Record, id: TypicalId));
        yield return Open("other-family", domain: User, token: token);
        yield return Open("payload-character-flipped", domain: Record, token: FlipPayloadCharacter(token));
        yield return Open("unknown-key-id", domain: Record, token: token.Replace(PublicIdFormat.TokenPrefix + active, PublicIdFormat.TokenPrefix + UnknownKeyId, StringComparison.Ordinal));
        yield return Open("upper-case-key-id", domain: Record, token: token.Replace(PublicIdFormat.TokenPrefix + active, PublicIdFormat.TokenPrefix + active.ToUpperInvariant(), StringComparison.Ordinal));
        yield return Open("truncated", domain: Record, token: token[..^1]);
        yield return Open("empty", domain: Record, token: string.Empty);
        var foreign = context with { Protocol = HeaderEdits.ForeignProtocol(context.Protocol) };
        yield return Open("minted-under-other-prefix", domain: Record, token: PublicIdVectors.Protect(foreign, keyId: active, domain: Record, id: TypicalId));
    }

    private static string FlipPayloadCharacter(string token)
    {
        var at = token.IndexOf(PublicIdFormat.Separator, PublicIdFormat.TokenPrefix.Length) + 1 + FlippedPayloadIndex;
        return token[..at] + (token[at] == 'A' ? 'B' : 'A') + token[(at + 1)..];
    }

    private static JsonObject Protect(string name, string keyId, string domain, long id) => new()
    {
        [CaseFields.Name] = name,
        [CaseFields.Kind] = CaseKinds.Protect,
        [PublicIdVectors.KeyIdField] = keyId,
        [PublicIdVectors.DomainField] = domain,
        [PublicIdVectors.IdField] = id.ToString(CultureInfo.InvariantCulture),
    };

    private static JsonObject Open(string name, string domain, string token) => new()
    {
        [CaseFields.Name] = name,
        [CaseFields.Kind] = CaseKinds.Open,
        [PublicIdVectors.DomainField] = domain,
        [PublicIdVectors.TokenField] = token,
    };
}

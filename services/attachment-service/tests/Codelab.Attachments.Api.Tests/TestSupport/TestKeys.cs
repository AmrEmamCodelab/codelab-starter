using System.Security.Cryptography;
using Codelab.Security.Keys;

namespace Codelab.Attachments.Api.Tests.TestSupport;

/// <summary>Key material generated per run (never committed, never a published fixture).</summary>
internal static class TestKeys
{
    private const int MaterialBytes = 32;

    /// <summary>A one-key ring with fresh random material.</summary>
    public static KeyRingOptions NewRing(string keyId)
    {
        var ring = new KeyRingOptions { ActiveKeyId = keyId };
        ring.Keys.Add(new KeyEntryOptions { KeyId = keyId, MaterialBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(MaterialBytes)) });
        return ring;
    }

    /// <summary>The ring as configuration entries under <paramref name="section"/>.</summary>
    public static IEnumerable<KeyValuePair<string, string?>> AsSettings(KeyRingOptions ring, string section)
    {
        yield return new($"{section}:ActiveKeyId", ring.ActiveKeyId);
        for (var index = 0; index < ring.Keys.Count; index++)
        {
            yield return new($"{section}:Keys:{index}:KeyId", ring.Keys[index].KeyId);
            yield return new($"{section}:Keys:{index}:MaterialBase64", ring.Keys[index].MaterialBase64);
        }
    }

    /// <summary>A fresh ES256 (P-256) key pair as PEM: PKCS#8 private, SubjectPublicKeyInfo public.</summary>
    public static (string PrivatePem, string PublicPem) NewServiceKeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportPkcs8PrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }
}

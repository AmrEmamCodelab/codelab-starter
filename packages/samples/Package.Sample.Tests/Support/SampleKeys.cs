using System.Security.Cryptography;

namespace Package.Sample.Tests.Support;

/// <summary>
/// Fresh key material for one sample host, generated at run time (no key is ever committed). The published-key boot
/// test swaps one of these for a starter fixture key and expects the boot to fail.
/// </summary>
internal sealed record SampleKeys(
    string PublicIdKey,
    string SigningMasterKey,
    string TokenSigningKey,
    string UserHashKey,
    string ServicePrivateKeyPem,
    string ServicePublicKeyPem)
{
    private const int KeyBytes = 32;

    public static SampleKeys Create()
    {
        using var service = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new SampleKeys(
            PublicIdKey: NewKey(),
            SigningMasterKey: NewKey(),
            TokenSigningKey: NewKey(),
            UserHashKey: NewKey(),
            ServicePrivateKeyPem: service.ExportPkcs8PrivateKeyPem(),
            ServicePublicKeyPem: service.ExportSubjectPublicKeyInfoPem());
    }

    public static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes));
}

using System.Security.Cryptography;
using Codelab.Security.Keys;

namespace Codelab.Security.Tests.TestSupport;

/// <summary>Fresh key material generated per test run. Nothing here is ever a committed key.</summary>
internal static class TestKeys
{
    internal const int KeyBytes = 32;

    internal static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes));

    internal static T Ring<T>(T ring, params string[] keyIds)
        where T : KeyRingOptions
    {
        foreach (var keyId in keyIds)
        {
            ring.Keys.Add(new KeyEntryOptions { KeyId = keyId, MaterialBase64 = NewKey() });
        }

        ring.ActiveKeyId = keyIds[0];
        return ring;
    }

    internal static KeyRingOptions Ring(params string[] keyIds) => Ring(new KeyRingOptions(), keyIds);

    internal static (string PrivatePem, string PublicPem) NewEcKeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportPkcs8PrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }
}

/// <summary>The protocols the tests run under: the fixed test prefix and a second one for prefix-binding checks.</summary>
internal static class TestProtocols
{
    internal const string AlternatePrefix = "ALTPFX";

    internal static readonly SecurityProtocol Test = SecurityProtocol.Create(SecurityProtocol.TestPrefix);

    internal static readonly SecurityProtocol Alternate = SecurityProtocol.Create(AlternatePrefix);
}

/// <summary>Identifier families used by the tests.</summary>
public sealed class Records : global::Codelab.Security.PublicIds.IIdDomain
{
    public static string Domain => "record";
}

/// <summary>A second family, to prove tokens do not cross families.</summary>
public sealed class Users : global::Codelab.Security.PublicIds.IIdDomain
{
    public static string Domain => "user";
}

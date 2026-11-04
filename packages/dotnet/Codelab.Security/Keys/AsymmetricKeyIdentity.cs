using System.Security.Cryptography;

namespace Codelab.Security.Keys;

/// <summary>
/// The canonical identity of an asymmetric key: its DER <c>SubjectPublicKeyInfo</c>. A PEM key has many spellings that
/// all import to the same key (one-line body, leading or trailing whitespace, CRLF, PKCS#8 or SEC1 "EC PRIVATE KEY",
/// the bare base64 body), and a private key and its public half name the same pair. Exporting the public key info
/// collapses every one of them to one byte sequence, so the published-material denylist matches the key, not its text.
/// </summary>
internal static class AsymmetricKeyIdentity
{
    private const string PemArmour = "-----BEGIN ";

    private static readonly Func<AsymmetricAlgorithm>[] Algorithms = [ECDsa.Create, RSA.Create];

    /// <summary>The <c>SubjectPublicKeyInfo</c> of a PEM key, or null when the text holds no importable key.</summary>
    internal static byte[]? FromPem(string value) =>
        value.Contains(PemArmour, StringComparison.Ordinal) ? FirstImport(algorithm => algorithm.ImportFromPem(value)) : null;

    /// <summary>
    /// The <c>SubjectPublicKeyInfo</c> of DER key bytes (a public key info, a PKCS#8 private key or a SEC1 EC private
    /// key), or null when the bytes are not a key.
    /// </summary>
    internal static byte[]? FromDer(byte[] der) =>
        FirstImport(algorithm => algorithm.ImportSubjectPublicKeyInfo(der, out _))
        ?? FirstImport(algorithm => algorithm.ImportPkcs8PrivateKey(der, out _))
        ?? FirstImport(algorithm => ((ECAlgorithm)algorithm).ImportECPrivateKey(der, out _), ecOnly: true);

    private static byte[]? FirstImport(Action<AsymmetricAlgorithm> import, bool ecOnly = false)
    {
        foreach (var create in ecOnly ? Algorithms[..1] : Algorithms)
        {
            using var algorithm = create();
            if (TryImport(algorithm, import))
            {
                return algorithm.ExportSubjectPublicKeyInfo();
            }
        }

        return null;
    }

    private static bool TryImport(AsymmetricAlgorithm algorithm, Action<AsymmetricAlgorithm> import)
    {
        try
        {
            import(algorithm);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            // Not a key of this algorithm (or not a key at all): the value simply has no asymmetric identity here.
            return false;
        }
    }
}

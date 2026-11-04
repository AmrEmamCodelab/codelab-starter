using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Codelab.Integration.Certificates;

/// <summary>SHA-256 thumbprint helpers shared by the options validator and the leaf validator.</summary>
internal static class Thumbprints
{
    /// <summary>Upper-case hex without separators, or null when the value is not a SHA-256 thumbprint.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var compact = string.Concat(value.Split(CertificateConstants.ThumbprintSeparators, StringSplitOptions.RemoveEmptyEntries));
        var isHex = compact.Length == CertificateConstants.Sha256ThumbprintHexLength && compact.All(char.IsAsciiHexDigit);
        return isHex ? compact.ToUpperInvariant() : null;
    }

    /// <summary>The SHA-256 thumbprint of a certificate as upper-case hex.</summary>
    public static string Sha256Of(X509Certificate certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
}

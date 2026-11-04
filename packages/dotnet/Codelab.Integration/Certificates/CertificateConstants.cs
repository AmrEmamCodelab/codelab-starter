namespace Codelab.Integration.Certificates;

/// <summary>Fixed values of the certificate checks.</summary>
internal static class CertificateConstants
{
    /// <summary>The TLS server authentication extended key usage (id-kp-serverAuth, RFC 5280).</summary>
    public const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    /// <summary>The length of a SHA-256 thumbprint written as hex.</summary>
    public const int Sha256ThumbprintHexLength = 64;

    /// <summary>Separators people copy along with a thumbprint from a certificate viewer.</summary>
    public static readonly char[] ThumbprintSeparators = [':', ' ', '-'];
}

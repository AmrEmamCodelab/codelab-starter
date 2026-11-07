namespace Starter.Modules.Ops.Domain;

/// <summary>
/// Why a health component or a certificate row is not fine. Read by the Ops Console, which owns the text; never an
/// exception message, a host name or a URL.
/// </summary>
public static class OpsFindingCodes
{
    /// <summary>The application database did not answer.</summary>
    public const string DatabaseUnreachable = "OPS_DATABASE_UNREACHABLE";

    /// <summary>A readiness probe did not answer within the probe timeout.</summary>
    public const string ProbeTimeout = "OPS_PROBE_TIMEOUT";

    /// <summary>A readiness probe could not connect (DNS, TCP or TLS failure, including an untrusted certificate).</summary>
    public const string ProbeUnreachable = "OPS_PROBE_UNREACHABLE";

    /// <summary>A readiness probe answered with a status other than 2xx.</summary>
    public const string ProbeNotReady = "OPS_PROBE_NOT_READY";

    /// <summary>A component answered ready, but slower than half the probe timeout.</summary>
    public const string ProbeSlow = "OPS_PROBE_SLOW";

    /// <summary>A configured certificate was not found in its store or file, or could not be read.</summary>
    public const string CertificateUnreadable = "OPS_CERTIFICATE_UNREADABLE";

    /// <summary>The identity provider's signing keys (JWKS) could not be fetched or parsed.</summary>
    public const string IdentityProviderKeysUnavailable = "OPS_IDP_KEYS_UNAVAILABLE";

    /// <summary>The identity provider published a signing key without a certificate (no <c>x5c</c>), so its end date is unknown.</summary>
    public const string IdentityProviderKeyWithoutCertificate = "OPS_IDP_KEY_WITHOUT_CERTIFICATE";
}

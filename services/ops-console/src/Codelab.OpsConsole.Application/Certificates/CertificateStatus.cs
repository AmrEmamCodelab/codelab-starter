namespace Codelab.OpsConsole.Application.Certificates;

/// <summary>
/// How close a certificate is to its expiry, as the API's /api/ops/certificates decides it from
/// <c>Codelab:Ops:CertificateWarningDays</c> and <c>CertificateCriticalDays</c>. Sent as the integer value.
/// </summary>
public enum CertificateStatus
{
    /// <summary>More than the warning window left.</summary>
    Ok = 1,

    /// <summary>Inside the warning window: plan the renewal.</summary>
    Warning = 2,

    /// <summary>Inside the critical window: renew now.</summary>
    Critical = 3,

    /// <summary>Past its NotAfter: sign-in or calls that depend on it fail.</summary>
    Expired = 4,
}

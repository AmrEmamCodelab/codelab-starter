namespace Starter.Modules.Ops.Domain.Certificates;

/// <summary>How close a certificate is to its end (sent as the integer value).</summary>
public enum CertificateStatus
{
    /// <summary>At least <c>Codelab:Ops:CertificateWarningDays</c> whole days left.</summary>
    Ok = 1,

    /// <summary>Fewer than the warning days left.</summary>
    Warning = 2,

    /// <summary>Fewer than <c>Codelab:Ops:CertificateCriticalDays</c> whole days left, or the end date cannot be read.</summary>
    Critical = 3,

    /// <summary>The end date has passed.</summary>
    Expired = 4,
}

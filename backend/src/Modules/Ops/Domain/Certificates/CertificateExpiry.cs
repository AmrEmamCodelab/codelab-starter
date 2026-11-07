namespace Starter.Modules.Ops.Domain.Certificates;

/// <summary>
/// The day thresholds of <see cref="CertificateExpiry"/>: fewer whole days left than <see cref="WarningDays"/> is
/// <see cref="CertificateStatus.Warning"/>, fewer than <see cref="CriticalDays"/> is <see cref="CertificateStatus.Critical"/>.
/// </summary>
/// <param name="WarningDays">The warning threshold in whole days (above <paramref name="CriticalDays"/>).</param>
/// <param name="CriticalDays">The critical threshold in whole days (at least 1).</param>
public sealed record CertificateThresholds(int WarningDays, int CriticalDays)
{
    /// <summary>The default warning threshold.</summary>
    public const int DefaultWarningDays = 30;

    /// <summary>The default critical threshold.</summary>
    public const int DefaultCriticalDays = 7;

    /// <summary>The highest warning threshold accepted.</summary>
    public const int MaxWarningDays = 365;

    /// <summary>True when 1 &lt;= critical &lt; warning &lt;= <see cref="MaxWarningDays"/>.</summary>
    public bool IsValid => CriticalDays >= 1 && CriticalDays < WarningDays && WarningDays <= MaxWarningDays;
}

/// <summary>A certificate's verdict: its status and the whole days left (null when the end date is unknown).</summary>
/// <param name="Status">The status.</param>
/// <param name="DaysLeft">Whole days left, rounded down (negative once expired), or null.</param>
public sealed record CertificateVerdict(CertificateStatus Status, int? DaysLeft);

/// <summary>The one owner of the certificate expiry rule.</summary>
public static class CertificateExpiry
{
    /// <summary>A certificate whose end date cannot be read: never ok, always <see cref="CertificateStatus.Critical"/>.</summary>
    public static CertificateVerdict Unreadable { get; } = new(CertificateStatus.Critical, DaysLeft: null);

    /// <summary>
    /// <paramref name="notAfter"/> against <paramref name="now"/>: <see cref="CertificateStatus.Expired"/> once passed,
    /// <see cref="CertificateStatus.Critical"/> under the critical days, <see cref="CertificateStatus.Warning"/> under the
    /// warning days, otherwise <see cref="CertificateStatus.Ok"/>. Whole days round down, so 6 days 23 hours is 6.
    /// </summary>
    public static CertificateVerdict Evaluate(DateTimeOffset notAfter, DateTimeOffset now, CertificateThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        var remaining = notAfter - now;
        var daysLeft = (int)Math.Floor(remaining.TotalDays);
        if (remaining <= TimeSpan.Zero)
        {
            return new CertificateVerdict(CertificateStatus.Expired, daysLeft);
        }

        if (daysLeft < thresholds.CriticalDays)
        {
            return new CertificateVerdict(CertificateStatus.Critical, daysLeft);
        }

        var status = daysLeft < thresholds.WarningDays ? CertificateStatus.Warning : CertificateStatus.Ok;
        return new CertificateVerdict(status, daysLeft);
    }
}

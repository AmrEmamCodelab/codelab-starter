namespace Codelab.OpsConsole.Application.Certificates;

/// <summary>
/// One certificate the API watches (for example the identity provider's signing certificate), as
/// <c>GET api/ops/certificates</c> reports it. A certificate the API could not read has no subject, end date or days
/// left, the status <see cref="CertificateStatus.Critical"/> and a finding <see cref="Code"/>.
/// </summary>
public sealed record CertificateExpiry
{
    /// <summary>The certificate's role, such as <c>idp-signing</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The certificate subject, or null when unreadable.</summary>
    public string? Subject { get; init; }

    /// <summary>When it expires, or null when unreadable.</summary>
    public DateTimeOffset? NotAfter { get; init; }

    /// <summary>Whole days until <see cref="NotAfter"/> (negative once expired), or null when unreadable.</summary>
    public int? DaysLeft { get; init; }

    /// <summary>The API's verdict.</summary>
    public required CertificateStatus Status { get; init; }

    /// <summary>Why the certificate could not be read (an <c>OPS_*</c> finding code), or null.</summary>
    public string? Code { get; init; }
}

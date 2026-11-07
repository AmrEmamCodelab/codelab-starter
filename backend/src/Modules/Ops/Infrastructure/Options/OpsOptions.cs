using Starter.Modules.Ops.Application;
using Starter.Modules.Ops.Domain.Certificates;

namespace Starter.Modules.Ops.Infrastructure.Options;

/// <summary>
/// <c>Codelab:Ops</c>: who may call <c>/api/ops</c>, the certificate thresholds, the components the health summary
/// probes, the certificates it watches, and the reference-code index. Checked at start by <see cref="OpsOptionsValidator"/>.
/// </summary>
public sealed class OpsOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Codelab:Ops";

    /// <summary>The default probe timeout in seconds.</summary>
    public const int DefaultProbeTimeoutSeconds = 5;

    /// <summary>The default log search margin either side of a sighting, in minutes.</summary>
    public const int DefaultSearchMarginMinutes = 15;

    /// <summary>The default sighting retention in days (the log retention of a default Seq install).</summary>
    public const int DefaultRetentionDays = 30;

    /// <summary>
    /// The key id (<c>kid</c>) of the Ops Console API's ES256 key, which must be one of
    /// <c>Codelab:Security:ServiceAssertions:Server:TrustedServices</c>. Empty: the Ops surface is closed (every call
    /// answers 401) and no server assertion settings are required.
    /// </summary>
    public string? TrustedCallerKeyId { get; set; }

    /// <summary>Fewer whole days left than this is a certificate warning. Default 30.</summary>
    public int CertificateWarningDays { get; set; } = CertificateThresholds.DefaultWarningDays;

    /// <summary>Fewer whole days left than this (below the warning days) is critical. Default 7.</summary>
    public int CertificateCriticalDays { get; set; } = CertificateThresholds.DefaultCriticalDays;

    /// <summary>How long one component check may take, in seconds (1 to 30). Default 5.</summary>
    public int ProbeTimeoutSeconds { get; set; } = DefaultProbeTimeoutSeconds;

    /// <summary>How far before and after an error the log search window reaches, in minutes (1 to 1440). Default 15.</summary>
    public int SearchMarginMinutes { get; set; } = DefaultSearchMarginMinutes;

    /// <summary>How long a reference-code sighting is kept, in days (1 to 365). Default 30.</summary>
    public int ReferenceCodeRetentionDays { get; set; } = DefaultRetentionDays;

    /// <summary>Record every problem+json error response in the reference-code index. Default true.</summary>
    public bool CaptureReferenceCodes { get; set; } = true;

    /// <summary>The services whose readiness URL the health summary probes (the attachment service, the gateway).</summary>
    public IList<ReadinessProbeOptions> ReadinessProbes { get; init; } = [];

    /// <summary>The TLS and signing certificates whose end dates the certificate list watches.</summary>
    public IList<WatchedCertificateOptions> Certificates { get; init; } = [];

    /// <summary>
    /// The identity provider's JWKS URL, read directly for the end dates of its signing certificates (<c>x5c</c>) only
    /// when single sign-on is not a login mode of this host; with <c>sso</c> on, the JWKS is read through the sign-in's
    /// own back channel (direct or the gateway relay) and this URL is ignored. The API must reach it over validated TLS.
    /// </summary>
    public string? IdentityProviderJwksUrl { get; set; }

    /// <summary>True when a trusted caller is configured, so the Ops surface is open to it.</summary>
    public bool IsOpen => !string.IsNullOrWhiteSpace(TrustedCallerKeyId);

    /// <summary>The settings the use cases read.</summary>
    public OpsSettings ToSettings() =>
        new(
            Thresholds: new CertificateThresholds(WarningDays: CertificateWarningDays, CriticalDays: CertificateCriticalDays),
            SearchMargin: TimeSpan.FromMinutes(SearchMarginMinutes),
            SightingRetention: TimeSpan.FromDays(ReferenceCodeRetentionDays));
}

/// <summary>A service the health summary probes: <c>GET</c> on its readiness URL, healthy on 2xx.</summary>
public sealed class ReadinessProbeOptions
{
    /// <summary>The component name (lowercase kebab), for example <c>attachment-service</c> or <c>integration-gateway</c>.</summary>
    public string? Name { get; set; }

    /// <summary>The absolute readiness URL, for example <c>https://attachments.example.test/health/ready</c>.</summary>
    public string? Url { get; set; }
}

/// <summary>
/// A certificate the list watches: from the Windows certificate store by thumbprint, or from a public certificate
/// file (<c>.cer</c>/<c>.crt</c>/<c>.pem</c>, never a private key). Exactly one of <see cref="Thumbprint"/> and
/// <see cref="Path"/>.
/// </summary>
public sealed class WatchedCertificateOptions
{
    /// <summary>The row name (lowercase kebab), for example <c>api-tls</c>.</summary>
    public string? Name { get; set; }

    /// <summary>The thumbprint in the store (hex, spaces ignored).</summary>
    public string? Thumbprint { get; set; }

    /// <summary>The store name (<c>My</c>, <c>Root</c>, ...). Default <c>My</c>.</summary>
    public string StoreName { get; set; } = nameof(System.Security.Cryptography.X509Certificates.StoreName.My);

    /// <summary>The store location (<c>LocalMachine</c> or <c>CurrentUser</c>). Default <c>LocalMachine</c>.</summary>
    public string StoreLocation { get; set; } = nameof(System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine);

    /// <summary>The path of a public certificate file.</summary>
    public string? Path { get; set; }
}

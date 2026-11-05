using Microsoft.Extensions.Options;

namespace Codelab.Identity.ActiveDirectory;

/// <summary>
/// Reads the certificate the configured directory presents on its LDAPS port, for an expiry list or a health page.
/// A TCP connect and a TLS handshake; no bind and nothing sent. A certificate this host would reject (untrusted chain,
/// name mismatch, expired) is still reported, with its <see cref="LdapsCertificateInfo.PolicyErrors"/>.
/// </summary>
public sealed class LdapsCertificateReader
{
    private readonly IOptions<ActiveDirectoryOptions> options;
    private readonly TimeProvider timeProvider;

    /// <summary>A reader over the bound <see cref="ActiveDirectoryOptions"/>.</summary>
    public LdapsCertificateReader(IOptions<ActiveDirectoryOptions> options, TimeProvider timeProvider)
    {
        this.options = options;
        this.timeProvider = timeProvider;
    }

    /// <summary>
    /// The presented certificate, or null when the provider is off, not LDAPS, has no host, or no handshake got far
    /// enough to see a certificate. Bounded by <see cref="ActiveDirectoryOptions.TimeoutSeconds"/>; cancellation propagates.
    /// </summary>
    public async Task<LdapsCertificateInfo?> ReadAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || !settings.UseLdaps || string.IsNullOrWhiteSpace(settings.Host))
        {
            return null;
        }

        var inspection = await TlsEndpointInspector.InspectAsync(EndpointOf(settings), timeProvider, cancellationToken);
        return inspection.Certificate;
    }

    internal static LdapEndpoint EndpointOf(ActiveDirectoryOptions settings) =>
        new(settings.Host, settings.Port, settings.UseLdaps, settings.Timeout);
}

/// <summary>Which layer a probe of the directory reached.</summary>
public enum DirectoryProbeStatus
{
    /// <summary>TCP connected and, for LDAPS, the certificate validated. A failure above this is LDAP or credentials.</summary>
    Reachable = 1,

    /// <summary>The provider is off or has no host: nothing was probed.</summary>
    NotConfigured = 2,

    /// <summary>Nothing answered on the port within the timeout (routing; firewall; not listening).</summary>
    TcpUnreachable = 3,

    /// <summary>TCP connected but TLS failed or the certificate is not trusted here; a sign-in would fail the same way.</summary>
    TlsRejected = 4,
}

/// <summary>A probe's answer: the layer reached, a one-line detail (endpoint and failure kind only) and the certificate seen.</summary>
public sealed record DirectoryProbeResult(DirectoryProbeStatus Status, string Detail, LdapsCertificateInfo? Certificate, TimeSpan Elapsed);

/// <summary>
/// A diagnostic for an operator: can this host reach the configured directory, at which layer does it stop. It sends
/// nothing (no bind, no DN, no password) and logs nothing; the caller decides who may see the detail.
/// </summary>
public interface IActiveDirectoryConnectionProbe
{
    /// <summary>Probes the configured endpoint, bounded by its timeout. Never throws for a network fault; cancellation propagates.</summary>
    Task<DirectoryProbeResult> ProbeAsync(CancellationToken cancellationToken);
}

/// <summary>The production probe over <see cref="TlsEndpointInspector"/>.</summary>
internal sealed class ActiveDirectoryConnectionProbe(IOptions<ActiveDirectoryOptions> options, TimeProvider timeProvider)
    : IActiveDirectoryConnectionProbe
{
    private const string NotConfiguredDetail = "The Active Directory provider is not enabled or has no host; nothing was probed.";

    public async Task<DirectoryProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Host))
        {
            return new DirectoryProbeResult(DirectoryProbeStatus.NotConfigured, NotConfiguredDetail, Certificate: null, Elapsed: TimeSpan.Zero);
        }

        var started = timeProvider.GetTimestamp();
        var endpoint = LdapsCertificateReader.EndpointOf(settings);
        var inspection = await TlsEndpointInspector.InspectAsync(endpoint, timeProvider, cancellationToken);
        var (status, detail) = Classify(endpoint, inspection);
        return new DirectoryProbeResult(status, detail, inspection.Certificate, timeProvider.GetElapsedTime(started));
    }

    internal static (DirectoryProbeStatus Status, string Detail) Classify(LdapEndpoint endpoint, EndpointInspection inspection)
    {
        var target = $"{endpoint.Host}:{endpoint.Port}";
        if (!inspection.TcpConnected)
        {
            return (DirectoryProbeStatus.TcpUnreachable, $"TCP connect to {target} failed: {inspection.Failure}.");
        }

        if (!endpoint.UseLdaps)
        {
            return (DirectoryProbeStatus.Reachable, $"TCP connect to {target} succeeded; no TLS (UseLdaps is false).");
        }

        if (inspection.TlsEstablished)
        {
            return (DirectoryProbeStatus.Reachable, $"TCP and TLS to {target} succeeded and the certificate validates.");
        }

        var errors = inspection.Certificate?.PolicyErrors.ToString() ?? inspection.Failure;
        return (DirectoryProbeStatus.TlsRejected, $"TLS to {target} failed: {errors}.");
    }
}

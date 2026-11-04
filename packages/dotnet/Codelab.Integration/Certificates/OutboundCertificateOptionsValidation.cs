namespace Codelab.Integration.Certificates;

/// <summary>The certificate part of the outbound options validation.</summary>
internal static class OutboundCertificateOptionsValidation
{
    public static void Validate(OutboundCertificateOptions certificate, List<string> failures)
    {
        switch (certificate.Mode)
        {
            case CertificateTrustMode.SystemTrust:
                ValidateSystemTrust(certificate, failures);
                break;
            case CertificateTrustMode.PinnedAuthority:
                ValidatePinnedAuthority(certificate, failures);
                break;
            case CertificateTrustMode.PinnedLeaf:
                ValidatePinnedLeaf(certificate, failures);
                break;
            default:
                failures.Add($"Certificate.Mode {(int)certificate.Mode} is not a known trust mode.");
                break;
        }
    }

    // Pins next to SystemTrust would be silently ignored, giving a false sense of pinning.
    private static void ValidateSystemTrust(OutboundCertificateOptions certificate, List<string> failures)
    {
        var hasPins = certificate.PinnedThumbprints.Count > 0
            || certificate.AuthorityCertificates.Count > 0
            || !string.IsNullOrWhiteSpace(certificate.AuthorityBundlePath);
        if (hasPins)
        {
            failures.Add("Certificate pins or an authority bundle are set but Certificate.Mode is SystemTrust; choose PinnedAuthority or PinnedLeaf.");
        }
    }

    private static void ValidatePinnedAuthority(OutboundCertificateOptions certificate, List<string> failures)
    {
        var hasBundle = !string.IsNullOrWhiteSpace(certificate.AuthorityBundlePath);
        if (!hasBundle && certificate.AuthorityCertificates.Count == 0)
        {
            failures.Add("Certificate.Mode PinnedAuthority needs AuthorityBundlePath or AuthorityCertificates.");
            return;
        }

        if (hasBundle && !File.Exists(certificate.AuthorityBundlePath))
        {
            failures.Add($"Certificate.AuthorityBundlePath '{certificate.AuthorityBundlePath}' does not exist.");
        }
    }

    private static void ValidatePinnedLeaf(OutboundCertificateOptions certificate, List<string> failures)
    {
        if (certificate.PinnedThumbprints.Count == 0)
        {
            failures.Add("Certificate.Mode PinnedLeaf needs at least one PinnedThumbprints entry.");
            return;
        }

        if (certificate.PinnedThumbprints.Any(thumbprint => Thumbprints.Normalize(thumbprint) is null))
        {
            failures.Add($"Every Certificate.PinnedThumbprints entry must be a SHA-256 thumbprint ({CertificateConstants.Sha256ThumbprintHexLength} hex characters).");
        }
    }
}

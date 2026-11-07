using Codelab.Integration.Certificates;
using Codelab.Integration.Outbound;

namespace Codelab.Gateway.Api.Relay;

/// <summary>
/// The outbound policy every upstream section carries (<c>Retry</c>, <c>Timeout</c>, <c>Certificate</c>), applied to the
/// profile's Codelab.Integration outbound client. Certificate validation is always on; only the trust anchor varies
/// (system trust, a pinned internal authority, or pinned leaf thumbprints).
/// </summary>
internal abstract class UpstreamOptions
{
    /// <summary>The retry policy. Only GETs are retried (a token exchange or an SMS is never sent twice).</summary>
    public OutboundRetryOptions Retry { get; set; } = new();

    /// <summary>The attempt and total timeouts.</summary>
    public OutboundTimeoutOptions Timeout { get; set; } = new();

    /// <summary>How the upstream certificate is trusted.</summary>
    public OutboundCertificateOptions Certificate { get; set; } = new();
}

/// <summary>Checks an upstream address: absolute, no credentials or fragment, https (http only in Development).</summary>
internal static class UpstreamAddress
{
    /// <summary>The problem with <paramref name="address"/>, or null when it is usable.</summary>
    public static string? Problem(Uri? address, string setting, bool isDevelopment)
    {
        if (address is null || !address.IsAbsoluteUri)
        {
            return $"{setting} is required and must be an absolute URL.";
        }

        if (address.Fragment.Length > 0 || address.UserInfo.Length > 0)
        {
            return $"{setting} must not carry a fragment or user credentials.";
        }

        var isAllowed = address.Scheme == Uri.UriSchemeHttps || (isDevelopment && address.Scheme == Uri.UriSchemeHttp);
        return isAllowed ? null : $"{setting} must use https (http is allowed only in Development).";
    }
}

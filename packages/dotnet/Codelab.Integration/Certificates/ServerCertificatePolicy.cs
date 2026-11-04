using System.Collections.Frozen;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Codelab.Integration.Certificates;

/// <summary>
/// Turns <see cref="OutboundCertificateOptions"/> into the TLS settings of the primary handler. This is the only place
/// that sets a certificate validation callback, and every callback it returns refuses by default.
/// </summary>
internal static class ServerCertificatePolicy
{
    /// <summary>The primary handler of an outbound client, with certificate validation configured from the options.</summary>
    public static SocketsHttpHandler CreatePrimaryHandler(OutboundCertificateOptions options, TimeProvider timeProvider)
    {
        var handler = new SocketsHttpHandler();
        handler.SslOptions.CertificateRevocationCheckMode = RevocationModeOf(options);
        handler.SslOptions.RemoteCertificateValidationCallback = CreateCallback(options, timeProvider);
        return handler;
    }

    /// <summary>
    /// Null for <see cref="CertificateTrustMode.SystemTrust"/>: no callback means .NET's own chain validation against
    /// the system store, which is the strongest default and cannot be weakened by a bug here.
    /// </summary>
    public static RemoteCertificateValidationCallback? CreateCallback(OutboundCertificateOptions options, TimeProvider timeProvider)
    {
        var validator = CreateValidator(options, timeProvider);
        if (validator is null)
        {
            return null;
        }

        return (_, certificate, chain, errors) => validator.Validate(certificate, chain, errors);
    }

    internal static IServerCertificateValidator? CreateValidator(OutboundCertificateOptions options, TimeProvider timeProvider) =>
        options.Mode switch
        {
            CertificateTrustMode.SystemTrust => null,
            CertificateTrustMode.PinnedAuthority => new PinnedAuthorityValidator(LoadAuthorities(options), RevocationModeOf(options)),
            CertificateTrustMode.PinnedLeaf => new PinnedLeafValidator(PinnedSet(options), timeProvider),
            _ => throw new InvalidOperationException($"Unknown certificate trust mode {(int)options.Mode}; the options validator should have refused it."),
        };

    internal static X509Certificate2Collection LoadAuthorities(OutboundCertificateOptions options)
    {
        var authorities = new X509Certificate2Collection();
        authorities.AddRange(options.AuthorityCertificates);
        if (!string.IsNullOrWhiteSpace(options.AuthorityBundlePath))
        {
            authorities.ImportFromPemFile(options.AuthorityBundlePath);
        }

        return authorities;
    }

    private static FrozenSet<string> PinnedSet(OutboundCertificateOptions options) =>
        options.PinnedThumbprints
            .Select(Thumbprints.Normalize)
            .OfType<string>()
            .ToFrozenSet(StringComparer.Ordinal);

    private static X509RevocationMode RevocationModeOf(OutboundCertificateOptions options) =>
        options.CheckRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck;
}

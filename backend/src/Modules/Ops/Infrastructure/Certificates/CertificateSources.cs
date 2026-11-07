using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Starter.Application.SingleSignOn;
using Starter.Modules.Ops.Application;
using Starter.Modules.Ops.Domain;
using Starter.Modules.Ops.Infrastructure.Health;
using Starter.Modules.Ops.Infrastructure.Options;

namespace Starter.Modules.Ops.Infrastructure.Certificates;

/// <summary>
/// The certificates listed in <c>Codelab:Ops:Certificates</c>: from the Windows certificate store by thumbprint, or
/// from a public certificate file. Only the public certificate is read, never a private key. A certificate that is not
/// found or does not parse is a row with <see cref="OpsFindingCodes.CertificateUnreadable"/>.
/// </summary>
public sealed class ConfiguredCertificateSource(IOptions<OpsOptions> options) : ICertificateSource
{
    /// <inheritdoc />
    public Task<IReadOnlyList<CertificateFact>> ReadAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CertificateFact> facts = [.. options.Value.Certificates.Select(Read)];
        return Task.FromResult(facts);
    }

    /// <summary>Reads one configured certificate.</summary>
    public static CertificateFact Read(WatchedCertificateOptions watched)
    {
        ArgumentNullException.ThrowIfNull(watched);
        var name = watched.Name ?? string.Empty;
        try
        {
            using var certificate = string.IsNullOrWhiteSpace(watched.Path) ? FromStore(watched) : X509CertificateLoader.LoadCertificateFromFile(watched.Path);
            return certificate is null ? Unreadable(name) : CertificateFacts.Of(name, certificate);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return Unreadable(name);
        }
    }

    private static X509Certificate2? FromStore(WatchedCertificateOptions watched)
    {
        var storeName = Enum.Parse<StoreName>(watched.StoreName, ignoreCase: true);
        var location = Enum.Parse<StoreLocation>(watched.StoreLocation, ignoreCase: true);
        using var store = new X509Store(storeName, location);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var thumbprint = string.Concat(watched.Thumbprint!.Where(char.IsAsciiHexDigit));
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        try
        {
            return found.Count == 0 ? null : new X509Certificate2(found[0]);
        }
        finally
        {
            foreach (var certificate in found)
            {
                certificate.Dispose();
            }
        }
    }

    private static CertificateFact Unreadable(string name) => new(name, Subject: null, NotAfter: null, OpsFindingCodes.CertificateUnreadable);
}

/// <summary>
/// The identity provider's signing certificates, read from its JWKS: one row per signing key, named
/// <c>idp-signing-N</c> in the provider's order, dated by the first certificate of its <c>x5c</c> chain. While single
/// sign-on is a login mode the JWKS comes through the kernel <see cref="IIdentityProviderKeySet"/>, that is over the
/// sign-in's own back channel (direct, or the integration gateway relay in a backend zone without egress), and
/// <c>Codelab:Ops:IdentityProviderJwksUrl</c> is not used; otherwise that URL is fetched directly. A key without
/// <c>x5c</c> has no knowable end date (<see cref="OpsFindingCodes.IdentityProviderKeyWithoutCertificate"/>); an
/// unreachable or unreadable JWKS is one row <c>idp-signing</c> with
/// <see cref="OpsFindingCodes.IdentityProviderKeysUnavailable"/>. Nothing without single sign-on and without a URL.
/// </summary>
public sealed class IdentityProviderCertificateSource(
    HttpClient client,
    IOptions<OpsOptions> options,
    ProbeBudget budget,
    IIdentityProviderKeySet? keySet = null) : ICertificateSource
{
    /// <summary>The row name prefix.</summary>
    public const string NamePrefix = "idp-signing";

    /// <inheritdoc />
    public async Task<IReadOnlyList<CertificateFact>> ReadAsync(CancellationToken cancellationToken)
    {
        var url = options.Value.IdentityProviderJwksUrl;
        if (keySet is null && string.IsNullOrWhiteSpace(url))
        {
            return [];
        }

        using var timeout = new CancellationTokenSource(budget.Timeout, budget.Clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using var jwks = keySet is null ? await FetchAsync(new Uri(url!), linked.Token) : await ReadThroughSsoAsync(keySet, linked.Token);
            return jwks is null ? [Unavailable()] : JwksCertificates.Read(jwks.RootElement, NamePrefix);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return [Unavailable()];
        }
    }

    private async Task<JsonDocument> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        await using var body = await client.GetStreamAsync(url, cancellationToken);
        return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
    }

    private static async Task<JsonDocument?> ReadThroughSsoAsync(IIdentityProviderKeySet source, CancellationToken cancellationToken)
    {
        var json = await source.ReadJwksAsync(cancellationToken);
        return json is null ? null : JsonDocument.Parse(json);
    }

    private static CertificateFact Unavailable() =>
        new(NamePrefix, Subject: null, NotAfter: null, OpsFindingCodes.IdentityProviderKeysUnavailable);
}

/// <summary>Reads the signing certificates of a JWKS document (RFC 7517: <c>keys[].x5c[0]</c>, base64 DER).</summary>
public static class JwksCertificates
{
    /// <summary>The JWKS member that holds the keys.</summary>
    public const string KeysMember = "keys";

    /// <summary>The JWK member that holds the certificate chain.</summary>
    public const string ChainMember = "x5c";

    /// <summary>The JWK member that states the key's use.</summary>
    public const string UseMember = "use";

    /// <summary>The use of a signing key.</summary>
    public const string SignatureUse = "sig";

    /// <summary>One fact per signing key of <paramref name="jwks"/>, named <c>{prefix}-N</c> (1-based, in document order).</summary>
    /// <exception cref="JsonException">The document is not a JWKS.</exception>
    public static IReadOnlyList<CertificateFact> Read(JsonElement jwks, string prefix)
    {
        if (jwks.ValueKind is not JsonValueKind.Object || !jwks.TryGetProperty(KeysMember, out var keys) || keys.ValueKind is not JsonValueKind.Array)
        {
            throw new JsonException("The document is not a JWKS.");
        }

        return [.. keys.EnumerateArray().Where(IsSigningKey).Select((key, index) => ReadKey(key, $"{prefix}-{index + 1}"))];
    }

    private static bool IsSigningKey(JsonElement key) =>
        key.ValueKind is JsonValueKind.Object
        && (!key.TryGetProperty(UseMember, out var use) || string.Equals(use.GetString(), SignatureUse, StringComparison.Ordinal));

    private static CertificateFact ReadKey(JsonElement key, string name)
    {
        if (!key.TryGetProperty(ChainMember, out var chain) || chain.ValueKind is not JsonValueKind.Array || chain.GetArrayLength() == 0)
        {
            return new CertificateFact(name, Subject: null, NotAfter: null, OpsFindingCodes.IdentityProviderKeyWithoutCertificate);
        }

        try
        {
            using var certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(chain[0].GetString() ?? string.Empty));
            return CertificateFacts.Of(name, certificate);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or InvalidOperationException)
        {
            return new CertificateFact(name, Subject: null, NotAfter: null, OpsFindingCodes.CertificateUnreadable);
        }
    }
}

/// <summary>Turns a loaded certificate into a <see cref="CertificateFact"/>.</summary>
public static class CertificateFacts
{
    /// <summary>The name, subject and end date (UTC) of <paramref name="certificate"/>.</summary>
    public static CertificateFact Of(string name, X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
        return new CertificateFact(name, certificate.Subject, notAfter, FindingCode: null);
    }
}

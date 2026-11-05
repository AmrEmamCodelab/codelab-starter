using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Codelab.Identity.Oidc;

/// <summary>Where <see cref="OidcIdTokenValidator"/> gets the id_token signing keys.</summary>
public interface IIdTokenSigningKeySource
{
    /// <summary>
    /// The keys a token naming <paramref name="kid"/> (null when it names none) may be verified with. Null only when no
    /// usable key set could be obtained (the provider is then unavailable); an empty or non-matching set simply fails
    /// validation. Never throws for a provider fault; cancellation propagates.
    /// </summary>
    Task<IReadOnlyList<SecurityKey>?> GetSigningKeysAsync(string? kid, CancellationToken cancellationToken);
}

/// <summary>
/// Pinned public keys (<see cref="OidcOptions.PinnedKeys"/>), read once at construction. For a host with no route to the
/// provider's JWKS: a provider key rotation is a configuration change (pin both keys during the overlap).
/// </summary>
public sealed class PinnedSigningKeySource : IIdTokenSigningKeySource
{
    /// <summary>The shortest accepted RSA key, in bits.</summary>
    public const int MinRsaKeyBits = 2048;

    private const string PemMarker = "-----BEGIN";
    private const string PrivateKeyMarker = "PRIVATE KEY";

    private readonly IReadOnlyList<SecurityKey> keys;

    /// <summary>Reads every pinned key.</summary>
    /// <exception cref="InvalidOperationException">A key does not parse (the deployment guard refuses that at start).</exception>
    public PinnedSigningKeySource(IOptions<OidcOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var list = new List<SecurityKey>();
        foreach (var entry in options.Value.PinnedKeys)
        {
            if (!TryRead(entry, out var key, out var problem))
            {
                throw new InvalidOperationException($"A pinned OIDC signing key is unusable: {problem}.");
            }

            list.Add(key);
        }

        keys = list;
    }

    /// <inheritdoc />
    /// <remarks>Never null and never fetched: the pinned set, whatever <paramref name="kid"/> names.</remarks>
    public Task<IReadOnlyList<SecurityKey>?> GetSigningKeysAsync(string? kid, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SecurityKey>?>(keys);

    /// <summary>
    /// Reads one pinned key: an RSA (at least <see cref="MinRsaKeyBits"/>) or EC SubjectPublicKeyInfo, as PEM or base64
    /// DER. A private key is refused, so one pasted by mistake never verifies anything. <paramref name="problem"/> names
    /// what is wrong without echoing key material.
    /// </summary>
    public static bool TryRead(OidcPinnedKey entry, [NotNullWhen(true)] out SecurityKey? key, [NotNullWhen(false)] out string? problem)
    {
        ArgumentNullException.ThrowIfNull(entry);
        key = null;
        problem = Precheck(entry.PublicKey);
        if (problem is not null)
        {
            return false;
        }

        var kid = string.IsNullOrWhiteSpace(entry.Kid) ? null : entry.Kid.Trim();
        var text = entry.PublicKey.Trim();
        if (Import(RSA.Create, text) is { } rsa)
        {
            return AcceptRsa(rsa, kid, out key, out problem);
        }

        if (Import(ECDsa.Create, text) is { } ec)
        {
            key = new ECDsaSecurityKey(ec) { KeyId = kid };
            return true;
        }

        problem = "the value is not an RSA or EC public key (SubjectPublicKeyInfo PEM or base64 DER)";
        return false;
    }

    private static string? Precheck(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "PublicKey is empty";
        }

        return value.Contains(PrivateKeyMarker, StringComparison.OrdinalIgnoreCase)
            ? "PublicKey holds a private key; only the provider's public key belongs here"
            : null;
    }

    private static bool AcceptRsa(RSA rsa, string? kid, [NotNullWhen(true)] out SecurityKey? key, [NotNullWhen(false)] out string? problem)
    {
        if (rsa.KeySize < MinRsaKeyBits)
        {
            rsa.Dispose();
            key = null;
            problem = $"the RSA key is shorter than {MinRsaKeyBits} bits";
            return false;
        }

        key = new RsaSecurityKey(rsa) { KeyId = kid };
        problem = null;
        return true;
    }

    private static T? Import<T>(Func<T> create, string text) where T : AsymmetricAlgorithm
    {
        var algorithm = create();
        try
        {
            if (text.StartsWith(PemMarker, StringComparison.Ordinal))
            {
                algorithm.ImportFromPem(text);
            }
            else
            {
                algorithm.ImportSubjectPublicKeyInfo(Convert.FromBase64String(text), out _);
            }

            return algorithm;
        }
        catch (Exception failure) when (failure is CryptographicException or ArgumentException or FormatException)
        {
            algorithm.Dispose();
            return null;
        }
    }
}

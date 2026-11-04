using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Codelab.Security.Canonical;
using Codelab.Security.Keys;

namespace Codelab.Security.PublicIds;

/// <summary>The PublicId key ring (<c>Codelab:Security:PublicIds</c>). Key ids travel in tokens: <c>[a-z0-9]{1,8}</c>.</summary>
public sealed class PublicIdKeyringOptions : KeyRingOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = SecuritySections.PublicIds;
}

/// <summary>
/// The PublicId keys, each split by HKDF-SHA256 into an AES-256-GCM encryption key and an HMAC-SHA256 nonce key
/// (info = <c>CanonicalFraming.BuildUtf8("PUBLICID-KDF", "{Prefix}-PUBLICID-v1", keyId, "enc"|"mac")</c>). Tokens
/// minted under a retired-but-listed key still decrypt; a token naming a key outside the ring never falls back.
/// </summary>
public sealed class PublicIdKeyring
{
    /// <summary>The KDF profile.</summary>
    public const string KdfProfile = "PUBLICID-KDF";

    private const string EncryptionPurpose = "enc";
    private const string MacPurpose = "mac";

    private readonly Dictionary<string, PublicIdSubkeys> _entries;

    private PublicIdKeyring(Dictionary<string, PublicIdSubkeys> entries, string activeKeyId)
    {
        _entries = entries;
        ActiveKeyId = activeKeyId;
    }

    /// <summary>The key new tokens are minted under.</summary>
    public string ActiveKeyId { get; }

    /// <summary>The key ids in the ring.</summary>
    public IReadOnlyCollection<string> KeyIds => _entries.Keys;

    internal PublicIdSubkeys Active => _entries[ActiveKeyId];

    /// <summary>Builds the ring, or throws when the ring is structurally invalid or a key id has the wrong shape.</summary>
    /// <exception cref="InvalidOperationException">The options are invalid (the message names the setting, never the value).</exception>
    public static PublicIdKeyring Create(KeyRingOptions options, SecurityProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(protocol);
        var verdict = Validate(options);
        if (!verdict.IsValid)
        {
            throw new InvalidOperationException(verdict.Error);
        }

        var framing = new CanonicalFraming(protocol);
        var entries = options.Keys.ToDictionary(
            key => key.KeyId!,
            key => DeriveSubkeys(framing, protocol, key),
            StringComparer.Ordinal);
        return new PublicIdKeyring(entries, options.ActiveKeyId!);
    }

    /// <summary>The structural verdict plus the key-id grammar.</summary>
    public static KeyMaterialResult Validate(KeyRingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var verdict = KeyMaterialValidator.ValidateRing(options, PublicIdKeyringOptions.SectionName);
        if (!verdict.IsValid)
        {
            return verdict;
        }

        var malformed = options.Keys.FirstOrDefault(key => !PublicIdFormat.IsWellFormedKeyId(key.KeyId));
        return malformed is null
            ? KeyMaterialResult.Ok()
            : KeyMaterialResult.Fail($"{PublicIdKeyringOptions.SectionName}:Keys[{malformed.KeyId}]:KeyId must match [a-z0-9]{{1,8}}; it travels inside the token.");
    }

    internal bool TryGet(string keyId, [NotNullWhen(true)] out PublicIdSubkeys? subkeys) => _entries.TryGetValue(keyId, out subkeys);

    private static PublicIdSubkeys DeriveSubkeys(CanonicalFraming framing, SecurityProtocol protocol, KeyEntryOptions key)
    {
        var material = Convert.FromBase64String(key.MaterialBase64!);
        try
        {
            return new PublicIdSubkeys(
                Derive(material, framing.BuildUtf8(KdfProfile, protocol.PublicIdSpec, key.KeyId!, EncryptionPurpose)),
                Derive(material, framing.BuildUtf8(KdfProfile, protocol.PublicIdSpec, key.KeyId!, MacPurpose)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    private static byte[] Derive(byte[] material, byte[] info) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, material, KeyMaterialValidator.MinimumKeyBytes, salt: null, info: info);
}

/// <summary>The derived encryption and nonce keys of one ring entry.</summary>
internal sealed record PublicIdSubkeys(byte[] Encryption, byte[] Mac);

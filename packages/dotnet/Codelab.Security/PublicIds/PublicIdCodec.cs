using System.Buffers.Binary;
using System.Security.Cryptography;
using Codelab.Security.Canonical;

namespace Codelab.Security.PublicIds;

/// <summary>Turns an internal identifier into an opaque token and back, bound to an identifier family.</summary>
public interface IPublicIdCodec
{
    /// <summary>The token for <paramref name="id"/> in <paramref name="domain"/> (deterministic for a key).</summary>
    string Protect(string domain, long id);

    /// <summary>The identifier inside <paramref name="token"/>; false for anything that does not authenticate.</summary>
    bool TryUnprotect(string domain, string? token, out long id);
}

/// <summary>
/// <c>{Prefix}-PUBLICID-v1</c>: AES-256-GCM over the big-endian id with AAD =
/// <c>CanonicalFraming.BuildUtf8("PUBLICID-AAD", "{Prefix}-PUBLICID-v1", keyId, domain)</c> and a synthetic nonce =
/// the first 12 bytes of HMAC-SHA256(macKey, AAD || plaintext). The nonce is checked again on decrypt, so the token is
/// deterministic per (key, domain, id) and has one spelling. Determinism leaks equality of ids by design (stable URLs).
/// </summary>
public sealed class PublicIdCodec : IPublicIdCodec
{
    /// <summary>The AAD profile.</summary>
    public const string AadProfile = "PUBLICID-AAD";

    private const int MaxAadBytes = 256;
    private const int DigestBytes = 32;

    private readonly PublicIdKeyring _keyring;
    private readonly SecurityProtocol _protocol;
    private readonly CanonicalFraming _framing;

    /// <summary>Creates the codec.</summary>
    public PublicIdCodec(PublicIdKeyring keyring, SecurityProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(keyring);
        ArgumentNullException.ThrowIfNull(protocol);
        _keyring = keyring;
        _protocol = protocol;
        _framing = new CanonicalFraming(protocol);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The domain is malformed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The id is below <see cref="PublicIdFormat.MinimumId"/>.</exception>
    public string Protect(string domain, long id)
    {
        RequireDomain(domain);
        ArgumentOutOfRangeException.ThrowIfLessThan(id, PublicIdFormat.MinimumId);

        var keyId = _keyring.ActiveKeyId;
        var subkeys = _keyring.Active;
        var aad = BuildAad(keyId, domain);
        Span<byte> plaintext = stackalloc byte[PublicIdFormat.PlaintextBytes];
        BinaryPrimitives.WriteInt64BigEndian(plaintext, id);

        Span<byte> payload = stackalloc byte[PublicIdFormat.PayloadBytes];
        var nonce = payload[..PublicIdFormat.NonceBytes];
        DeriveNonce(subkeys.Mac, aad, plaintext, nonce);
        using (var aes = new AesGcm(subkeys.Encryption, PublicIdFormat.TagBytes))
        {
            aes.Encrypt(nonce, plaintext, Ciphertext(payload), Tag(payload), aad);
        }

        return string.Concat(PublicIdFormat.TokenPrefix, keyId, PublicIdFormat.Separator.ToString(), Base64UrlText.Encode(payload));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The domain is malformed (a call-site constant, so a programming error).</exception>
    public bool TryUnprotect(string domain, string? token, out long id)
    {
        id = 0;
        RequireDomain(domain);
        if (!PublicIdFormat.TrySplit(token, out var keyId, out var encoded) || !_keyring.TryGet(keyId, out var subkeys))
        {
            return false;
        }

        Span<byte> payload = stackalloc byte[PublicIdFormat.PayloadBytes];
        Span<byte> plaintext = stackalloc byte[PublicIdFormat.PlaintextBytes];
        var aad = BuildAad(keyId, domain);
        if (!Base64UrlText.TryDecode(encoded, payload) || !TryDecrypt(subkeys, payload, aad, plaintext))
        {
            return false;
        }

        Span<byte> expectedNonce = stackalloc byte[PublicIdFormat.NonceBytes];
        DeriveNonce(subkeys.Mac, aad, plaintext, expectedNonce);
        var value = BinaryPrimitives.ReadInt64BigEndian(plaintext);
        if (!CryptographicOperations.FixedTimeEquals(payload[..PublicIdFormat.NonceBytes], expectedNonce) || value < PublicIdFormat.MinimumId)
        {
            return false;
        }

        id = value;
        return true;
    }

    private byte[] BuildAad(string keyId, string domain) => _framing.BuildUtf8(AadProfile, _protocol.PublicIdSpec, keyId, domain);

    private static bool TryDecrypt(PublicIdSubkeys subkeys, Span<byte> payload, byte[] aad, Span<byte> plaintext)
    {
        try
        {
            using var aes = new AesGcm(subkeys.Encryption, PublicIdFormat.TagBytes);
            aes.Decrypt(payload[..PublicIdFormat.NonceBytes], Ciphertext(payload), Tag(payload), plaintext, aad);
            return true;
        }
        catch (AuthenticationTagMismatchException)
        {
            return false;
        }
    }

    private static Span<byte> Tag(Span<byte> payload) => payload.Slice(PublicIdFormat.NonceBytes, PublicIdFormat.TagBytes);

    private static Span<byte> Ciphertext(Span<byte> payload) => payload[(PublicIdFormat.NonceBytes + PublicIdFormat.TagBytes)..];

    private static void RequireDomain(string domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if (!PublicIdFormat.IsWellFormedDomain(domain))
        {
            throw new ArgumentException("The domain must match [a-z][a-z0-9-]{0,31}; it is a call-site constant bound into the AAD.", nameof(domain));
        }
    }

    private static void DeriveNonce(byte[] macKey, byte[] aad, ReadOnlySpan<byte> plaintext, Span<byte> nonce)
    {
        if (aad.Length > MaxAadBytes)
        {
            throw new InvalidOperationException($"The PublicId AAD is {aad.Length} bytes, above the {MaxAadBytes}-byte bound.");
        }

        Span<byte> input = stackalloc byte[MaxAadBytes + PublicIdFormat.PlaintextBytes];
        aad.CopyTo(input);
        plaintext.CopyTo(input[aad.Length..]);
        Span<byte> digest = stackalloc byte[DigestBytes];
        HMACSHA256.HashData(macKey, input[..(aad.Length + plaintext.Length)], digest);
        digest[..PublicIdFormat.NonceBytes].CopyTo(nonce);
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Codelab.Security.Canonical;
using Codelab.Security.Keys;
using Codelab.Security.Signing;

namespace Codelab.Security.Uploads;

/// <summary>What a receipt attests: this file (id, size, SHA-256, name) was stored for this scope at this time.</summary>
public sealed record UploadReceipt(string Scope, string FileId, long SizeBytes, string Sha256Hex, string FileName, long IssuedAtUnix);

/// <summary>Signs and verifies upload receipts.</summary>
public interface IUploadReceiptSigner
{
    /// <summary>The receipt token for <paramref name="receipt"/>.</summary>
    string Sign(UploadReceipt receipt);

    /// <summary>The receipt inside <paramref name="token"/> when its MAC verifies under a ring key.</summary>
    bool TryVerify(string? token, [NotNullWhen(true)] out UploadReceipt? receipt);
}

/// <summary>
/// <c>{Prefix}-RECEIPT-v1</c>: <c>r1.{keyId}.{base64url(json)}.{mac}</c>, where the MAC is HMAC-SHA256 under a key
/// derived (HKDF) from the ring entry, over <c>CanonicalFraming.BuildUtf8("RECEIPT", spec, keyId, payload)</c>. The key
/// is raw key bytes, never the UTF-8 text of a secret. Verification is constant time; rotation keeps old keys listed.
/// </summary>
public sealed class UploadReceiptSigner : IUploadReceiptSigner
{
    /// <summary>The token version marker.</summary>
    public const string TokenVersion = "r1";

    private const char Separator = '.';
    private const int PartCount = 4;
    private const int VersionPart = 0;
    private const int KeyIdPart = 1;
    private const int PayloadPart = 2;
    private const int MacPart = 3;
    private const string MacProfile = "RECEIPT";
    private const string KdfProfile = "RECEIPT-KDF";

    private readonly Dictionary<string, byte[]> _keys;
    private readonly string _activeKeyId;
    private readonly SecurityProtocol _protocol;
    private readonly CanonicalFraming _framing;

    /// <summary>Creates the signer from a validated ring.</summary>
    /// <exception cref="InvalidOperationException">The ring is structurally invalid.</exception>
    public UploadReceiptSigner(KeyRingOptions ring, SecurityProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(ring);
        _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
        var verdict = KeyMaterialValidator.ValidateRing(ring, UploadSafetyOptions.SectionName);
        if (!verdict.IsValid)
        {
            throw new InvalidOperationException(verdict.Error);
        }

        _framing = new CanonicalFraming(protocol);
        _keys = ring.Keys.ToDictionary(
            key => key.KeyId!,
            key => HKDF.DeriveKey(HashAlgorithmName.SHA256, Convert.FromBase64String(key.MaterialBase64!), KeyMaterialValidator.MinimumKeyBytes, salt: null, info: _framing.BuildUtf8(KdfProfile, protocol.ReceiptSpec, key.KeyId!)),
            StringComparer.Ordinal);
        _activeKeyId = ring.ActiveKeyId!;
    }

    /// <inheritdoc />
    public string Sign(UploadReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var payload = Base64UrlText.Encode(JsonSerializer.SerializeToUtf8Bytes(receipt));
        var mac = HmacSignature.Compute(_keys[_activeKeyId], MacInput(_activeKeyId, payload));
        return string.Join(Separator, TokenVersion, _activeKeyId, payload, mac);
    }

    /// <inheritdoc />
    public bool TryVerify(string? token, [NotNullWhen(true)] out UploadReceipt? receipt)
    {
        receipt = null;
        var parts = token?.Split(Separator) ?? [];
        if (parts.Length != PartCount || parts[VersionPart] != TokenVersion || !_keys.TryGetValue(parts[KeyIdPart], out var key)
            || !HmacSignature.Verify(key, MacInput(parts[KeyIdPart], parts[PayloadPart]), parts[MacPart]) || !Base64UrlText.TryDecode(parts[PayloadPart], out var json))
        {
            return false;
        }

        receipt = JsonSerializer.Deserialize<UploadReceipt>(json);
        return receipt is not null;
    }

    private byte[] MacInput(string keyId, string payload) => _framing.BuildUtf8(MacProfile, _protocol.ReceiptSpec, keyId, payload);
}

using System.Security.Cryptography;
using System.Text.Json;
using Codelab.Security.Canonical;
using Codelab.Security.Keys;
using Codelab.Security.Replay;

namespace Codelab.Security.Tickets;

/// <summary>
/// <c>{Prefix}-TICKET-v1</c>: <c>t1.{keyId}.{base64url(nonce || ciphertext || tag)}</c>, AES-256-GCM with a random
/// nonce over a JSON body (id, subject, expiry, data), AAD = <c>CanonicalFraming.BuildUtf8("TICKET-AAD", spec, keyId,
/// purpose)</c> so a ticket sealed for one purpose never opens for another. Expiry comes from <see cref="TimeProvider"/>;
/// single use comes from the replay guard (scope <c>ticket/{purpose}</c>).
/// </summary>
public sealed class SealedTicketService : ISealedTicketService
{
    /// <summary>The token version marker.</summary>
    public const string TokenVersion = "t1";

    private const char Separator = '.';
    private const int TokenParts = 3;
    private const int VersionPart = 0;
    private const int KeyIdPart = 1;
    private const int SealedPart = 2;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int IdBytes = 16;
    private const string AadProfile = "TICKET-AAD";
    private const string KdfProfile = "TICKET-KDF";
    private const string ReplayScopePrefix = "ticket/";

    private readonly Dictionary<string, byte[]> _keys;
    private readonly string _activeKeyId;
    private readonly TimeSpan _maxLifetime;
    private readonly IReplayGuard _replayGuard;
    private readonly TimeProvider _clock;
    private readonly SecurityProtocol _protocol;
    private readonly CanonicalFraming _framing;

    /// <summary>Creates the service from a validated ring.</summary>
    /// <exception cref="InvalidOperationException">The ring is structurally invalid.</exception>
    public SealedTicketService(SealedTicketOptions options, IReplayGuard replayGuard, TimeProvider clock, SecurityProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(protocol);
        var verdict = KeyMaterialValidator.ValidateRing(options, SealedTicketOptions.SectionName);
        if (!verdict.IsValid)
        {
            throw new InvalidOperationException(verdict.Error);
        }

        _protocol = protocol;
        _framing = new CanonicalFraming(protocol);
        _keys = options.Keys.ToDictionary(key => key.KeyId!, key => DeriveKey(key), StringComparer.Ordinal);
        _activeKeyId = options.ActiveKeyId!;
        _maxLifetime = TimeSpan.FromSeconds(options.MaxLifetimeSeconds);
        _replayGuard = replayGuard ?? throw new ArgumentNullException(nameof(replayGuard));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Purpose or subject is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The lifetime is not positive or exceeds the configured maximum.</exception>
    public string Seal(TicketRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Purpose, nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Subject, nameof(request));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(request.Lifetime, TimeSpan.Zero, nameof(request));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.Lifetime, _maxLifetime, nameof(request));

        var body = new TicketBody(
            Id: Base64UrlText.Encode(RandomNumberGenerator.GetBytes(IdBytes)),
            Subject: request.Subject,
            ExpiresAtUnix: (_clock.GetUtcNow() + request.Lifetime).ToUnixTimeSeconds(),
            Data: request.Data);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(body);
        var sealedBytes = new byte[NonceBytes + plaintext.Length + TagBytes];
        RandomNumberGenerator.Fill(sealedBytes.AsSpan(0, NonceBytes));
        using (var aes = new AesGcm(_keys[_activeKeyId], TagBytes))
        {
            aes.Encrypt(sealedBytes.AsSpan(0, NonceBytes), plaintext, sealedBytes.AsSpan(NonceBytes, plaintext.Length), sealedBytes.AsSpan(NonceBytes + plaintext.Length), Aad(_activeKeyId, request.Purpose));
        }

        return string.Join(Separator, TokenVersion, _activeKeyId, Base64UrlText.Encode(sealedBytes));
    }

    /// <inheritdoc />
    public async Task<TicketOpenResult> OpenAsync(string purpose, string token, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        if (TryUnseal(purpose, token) is not { } body)
        {
            return TicketOpenResult.Refused(TicketOpenStatus.Invalid);
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(body.ExpiresAtUnix);
        if (expiresAt <= _clock.GetUtcNow())
        {
            return TicketOpenResult.Refused(TicketOpenStatus.Expired);
        }

        var outcome = await _replayGuard.TryConsumeAsync(new ReplayNonce(ReplayScopePrefix + purpose, body.Id, expiresAt), cancellationToken);
        return outcome switch
        {
            NonceConsumeOutcome.Fresh => new TicketOpenResult(TicketOpenStatus.Valid, new SealedTicket(body.Id, purpose, body.Subject, expiresAt, body.Data)),
            NonceConsumeOutcome.Replayed => TicketOpenResult.Refused(TicketOpenStatus.AlreadyUsed),
            _ => TicketOpenResult.Refused(TicketOpenStatus.StoreUnavailable),
        };
    }

    private TicketBody? TryUnseal(string purpose, string? token)
    {
        var parts = token?.Split(Separator) ?? [];
        if (parts.Length != TokenParts || parts[VersionPart] != TokenVersion || !_keys.TryGetValue(parts[KeyIdPart], out var key) || !TryDecodeSealed(parts[SealedPart], out var sealedBytes))
        {
            return null;
        }

        var plaintextLength = sealedBytes.Length - NonceBytes - TagBytes;
        var plaintext = new byte[plaintextLength];
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(sealedBytes.AsSpan(0, NonceBytes), sealedBytes.AsSpan(NonceBytes, plaintextLength), sealedBytes.AsSpan(NonceBytes + plaintextLength), plaintext, Aad(parts[KeyIdPart], purpose));
            return JsonSerializer.Deserialize<TicketBody>(plaintext);
        }
        catch (AuthenticationTagMismatchException)
        {
            return null;
        }
    }

    private static bool TryDecodeSealed(string encoded, out byte[] sealedBytes) =>
        Base64UrlText.TryDecode(encoded, out sealedBytes) && sealedBytes.Length > NonceBytes + TagBytes;

    private byte[] Aad(string keyId, string purpose) => _framing.BuildUtf8(AadProfile, _protocol.TicketSpec, keyId, purpose);

    private byte[] DeriveKey(KeyEntryOptions key) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, Convert.FromBase64String(key.MaterialBase64!), KeyMaterialValidator.MinimumKeyBytes, salt: null, info: _framing.BuildUtf8(KdfProfile, _protocol.TicketSpec, key.KeyId!));

    private sealed record TicketBody(string Id, string Subject, long ExpiresAtUnix, string? Data);
}

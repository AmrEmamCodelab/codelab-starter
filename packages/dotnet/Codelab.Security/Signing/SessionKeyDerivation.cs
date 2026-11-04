using System.Security.Cryptography;
using Codelab.Security.Canonical;

namespace Codelab.Security.Signing;

/// <summary>The session a key or a nonce belongs to: the user and the session id (the <c>sid</c> claim).</summary>
public sealed record SessionIdentity(string UserId, string SessionId)
{
    /// <summary>The replay scope of this session: <c>{userId}.{sessionId}</c>.</summary>
    public string Scope => string.Concat(UserId, ".", SessionId);
}

/// <summary>
/// Derives the per-session signing key from a master key: HKDF-SHA256 with no salt and info =
/// <c>CanonicalFraming.BuildUtf8("{Prefix}-SIG-v1/KDF", "{Prefix}-SIG-v1", sessionId, userId)</c>, 32 bytes out.
/// Stateless: every replica derives the same key for the same session, and nothing is stored.
/// </summary>
public sealed class SessionKeyDerivation
{
    /// <summary>The derived key length.</summary>
    public const int KeyBytes = 32;

    private readonly SecurityProtocol _protocol;
    private readonly CanonicalFraming _framing;

    /// <summary>Creates the derivation for <paramref name="protocol"/>.</summary>
    public SessionKeyDerivation(SecurityProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        _protocol = protocol;
        _framing = new CanonicalFraming(protocol);
    }

    /// <summary>The 32-byte session key for <paramref name="session"/>.</summary>
    /// <exception cref="ArgumentException">The master key is shorter than 32 bytes, or an identity field is blank.</exception>
    public byte[] Derive(ReadOnlySpan<byte> masterKey, SessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.UserId, nameof(session));
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId, nameof(session));
        if (masterKey.Length < KeyBytes)
        {
            throw new ArgumentException($"The master signing key must be at least {KeyBytes} bytes.", nameof(masterKey));
        }

        var info = _framing.BuildUtf8(_protocol.SessionKeyProfile, _protocol.SignatureVersion, session.SessionId, session.UserId);
        var output = new byte[KeyBytes];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, output, salt: default, info);
        return output;
    }
}

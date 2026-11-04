using System.Security.Cryptography;
using Codelab.Security.Canonical;

namespace Codelab.Security.Replay;

/// <summary>The outcome of consuming a nonce.</summary>
public enum NonceConsumeOutcome
{
    /// <summary>Already consumed in this scope (or already expired): refuse.</summary>
    Replayed = 0,

    /// <summary>First use: proceed.</summary>
    Fresh = 1,

    /// <summary>The store could not answer: fail closed (503), never treat as fresh.</summary>
    StoreUnavailable = 2,
}

/// <summary>
/// A nonce to consume: unique within <see cref="Scope"/> until <see cref="ExpiresAt"/>. Scopes keep families apart
/// (a request proof is scoped to its session, a service assertion to its issuer, a ticket to its purpose), so one
/// family can never burn another's nonce.
/// </summary>
public sealed record ReplayNonce(string Scope, string Nonce, DateTimeOffset ExpiresAt)
{
    private const string KeyVersion = "codelab-replay-v1";
    private const string KeyProfile = "NONCE";

    /// <summary>The store key: SHA-256 of the framed (scope, nonce), so scope and nonce boundaries cannot shift.</summary>
    internal byte[] KeyHash() => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(CanonicalFraming.Compose(KeyVersion, KeyProfile, [Scope, Nonce])));
}

/// <summary>Single-use nonce store shared by request signing, service assertions and sealed tickets.</summary>
public interface IReplayGuard
{
    /// <summary>
    /// Consumes <paramref name="nonce"/> atomically: exactly one concurrent caller gets <see cref="NonceConsumeOutcome.Fresh"/>.
    /// A nonce whose expiry has already passed is reported <see cref="NonceConsumeOutcome.Replayed"/>.
    /// </summary>
    Task<NonceConsumeOutcome> TryConsumeAsync(ReplayNonce nonce, CancellationToken cancellationToken);
}

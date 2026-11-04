namespace Codelab.Security.Signing;

/// <summary>Why a request proof was refused. Each maps to one problem code (<see cref="Problems.SecurityProblemCodes"/>).</summary>
public enum ProofRejectionReason
{
    /// <summary>Malformed, wrong MAC, or an unsignable method.</summary>
    Invalid = 0,

    /// <summary>No proof header on an authenticated, non-exempt request.</summary>
    Missing = 1,

    /// <summary>The timestamp is outside the accepted window (either direction).</summary>
    Stale = 2,

    /// <summary>The token yields no signing key (no session claims, or a key id outside the ring).</summary>
    KeyUnbound = 3,

    /// <summary>The nonce was already consumed in this session.</summary>
    Replayed = 4,
}

/// <summary>What a valid proof establishes: its nonce and when it was issued.</summary>
public sealed record VerifiedProof(string Nonce, DateTimeOffset IssuedAt);

/// <summary>The verdict of <see cref="IProofVerifier.Verify"/>.</summary>
public sealed class ProofVerificationResult
{
    private ProofVerificationResult(VerifiedProof? verified, ProofRejectionReason? reason)
    {
        Verified = verified;
        Reason = reason;
    }

    /// <summary>True when the proof verified.</summary>
    public bool IsValid => Verified is not null;

    /// <summary>The refusal reason, when invalid.</summary>
    public ProofRejectionReason? Reason { get; }

    /// <summary>The verified proof, when valid.</summary>
    public VerifiedProof? Verified { get; }

    /// <summary>A valid verdict.</summary>
    public static ProofVerificationResult Valid(VerifiedProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        return new ProofVerificationResult(proof, reason: null);
    }

    /// <summary>A refusal.</summary>
    public static ProofVerificationResult Rejected(ProofRejectionReason reason) => new(verified: null, reason);
}

/// <summary>Everything the verifier needs from one request. <see cref="ToString"/> never prints the proof.</summary>
public sealed record ProofVerificationInput(
    string Method,
    string Path,
    string Query,
    string BodyHash,
    string Proof,
    string SessionKeyId,
    SessionIdentity? Session)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"{nameof(ProofVerificationInput)} {{ Method = {Method}, Path = {Path}, Query = {Query}, BodyHash = {BodyHash}, Proof = [redacted], SessionKeyId = {SessionKeyId}, Session = {Session} }}";
}

/// <summary>Verifies a request proof. Pure: no replay consumption (the middleware does that only after a valid proof).</summary>
public interface IProofVerifier
{
    /// <summary>Verifies <paramref name="input"/>; never throws for attacker-controlled input.</summary>
    ProofVerificationResult Verify(ProofVerificationInput input);
}

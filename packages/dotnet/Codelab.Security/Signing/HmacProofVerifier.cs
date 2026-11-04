namespace Codelab.Security.Signing;

/// <summary>
/// The <c>{Prefix}-SIG-v1</c> request proof verifier: header grammar, timestamp window (both directions), session
/// key derivation from the ring entry named by the token, then a constant-time MAC check over the request form.
/// </summary>
public sealed class HmacProofVerifier : IProofVerifier
{
    private readonly ISigningKeyring _keyring;
    private readonly TimeProvider _clock;
    private readonly string _audience;
    private readonly TimeSpan _maxAge;
    private readonly SignatureCanonicalForm _forms;
    private readonly SessionKeyDerivation _derivation;

    /// <summary>Creates the verifier.</summary>
    public HmacProofVerifier(ISigningKeyring keyring, TimeProvider clock, SecurityProtocol protocol, RequestSigningOptions options)
    {
        ArgumentNullException.ThrowIfNull(keyring);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        _keyring = keyring;
        _clock = clock;
        _audience = options.Audience ?? string.Empty;
        _maxAge = TimeSpan.FromSeconds(options.MaxAgeSeconds);
        _forms = new SignatureCanonicalForm(protocol);
        _derivation = new SessionKeyDerivation(protocol);
    }

    /// <inheritdoc />
    public ProofVerificationResult Verify(ProofVerificationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Proof))
        {
            return ProofVerificationResult.Rejected(ProofRejectionReason.Missing);
        }

        if (!SignatureHeader.TryParse(input.Proof, out var header) || !Canonical.CanonicalText.TryCanonicalMethod(input.Method, out _))
        {
            return ProofVerificationResult.Rejected(ProofRejectionReason.Invalid);
        }

        var issued = DateTimeOffset.FromUnixTimeSeconds(header.TimestampUnixSeconds);
        if ((_clock.GetUtcNow() - issued).Duration() > _maxAge)
        {
            return ProofVerificationResult.Rejected(ProofRejectionReason.Stale);
        }

        if (input.Session is null || !_keyring.TryGet(input.SessionKeyId, out var masterKey))
        {
            return ProofVerificationResult.Rejected(ProofRejectionReason.KeyUnbound);
        }

        var sessionKey = _derivation.Derive(masterKey, input.Session);
        return VerifyMac(input, header, sessionKey)
            ? ProofVerificationResult.Valid(new VerifiedProof(header.Nonce, issued))
            : ProofVerificationResult.Rejected(ProofRejectionReason.Invalid);
    }

    private bool VerifyMac(ProofVerificationInput input, SignatureHeader header, byte[] sessionKey)
    {
        var fields = new SignedRequestFields(
            Audience: _audience,
            Method: input.Method,
            Path: input.Path,
            Query: input.Query,
            BodyHash: input.BodyHash,
            Timestamp: header.Timestamp,
            Nonce: header.Nonce);
        return HmacSignature.Verify(sessionKey, _forms.RequestBytes(fields), header.Mac);
    }
}

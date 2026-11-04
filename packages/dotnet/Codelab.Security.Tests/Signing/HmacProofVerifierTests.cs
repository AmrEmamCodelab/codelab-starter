using System.Globalization;
using Codelab.Security.Signing;
using Codelab.Security.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace Codelab.Security.Tests.Signing;

public sealed class HmacProofVerifierTests
{
    private const string Audience = "Starter.Api";
    private static readonly TestSession Session = new("7", "session-a", "m1");
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero));
    private readonly RequestSigningOptions _options = TestKeys.Ring(new RequestSigningOptions { Mode = SecurityEnforcementMode.Enforce, Audience = Audience }, "m1");

    private HmacProofVerifier Verifier => new(SigningKeyring.Create(_options), _clock, TestProtocols.Test, _options);

    private RequestSigner Signer => RequestSigner.ForSession(TestProtocols.Test, _options.Keys[0].MaterialBase64!, Session, Audience);

    [Fact]
    public void A_correctly_signed_request_verifies()
    {
        var result = Verifier.Verify(Input(Proof(Fields())));

        Assert.True(result.IsValid);
        Assert.Equal(_clock.GetUtcNow(), result.Verified!.IssuedAt);
    }

    [Fact]
    public void An_absent_proof_is_missing_not_invalid()
    {
        Assert.Equal(ProofRejectionReason.Missing, Verifier.Verify(Input(string.Empty)).Reason);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("1.2.3.x")]
    [InlineData("1.946684800.NNNNNNNNNNNNNNNNNNNNNN.###########################################")]
    public void A_malformed_header_is_rejected_without_throwing(string proof)
    {
        Assert.Equal(ProofRejectionReason.Invalid, Verifier.Verify(Input(proof)).Reason);
    }

    [Theory]
    [InlineData(-121)]
    [InlineData(121)]
    public void A_proof_outside_the_window_is_stale_in_either_direction(int offsetSeconds)
    {
        var proof = Proof(Fields() with { Timestamp = Unix(_clock.GetUtcNow().AddSeconds(offsetSeconds)) });

        Assert.Equal(ProofRejectionReason.Stale, Verifier.Verify(Input(proof)).Reason);
    }

    [Theory]
    [InlineData(-120)]
    [InlineData(120)]
    public void A_proof_at_the_window_edge_is_accepted_in_both_directions(int offsetSeconds)
    {
        var proof = Proof(Fields() with { Timestamp = Unix(_clock.GetUtcNow().AddSeconds(offsetSeconds)) });

        Assert.True(Verifier.Verify(Input(proof)).IsValid);
    }

    [Fact]
    public void A_key_id_naming_no_ring_entry_or_a_missing_session_is_key_unbound()
    {
        var proof = Proof(Fields());

        Assert.Equal(ProofRejectionReason.KeyUnbound, Verifier.Verify(Input(proof) with { SessionKeyId = "retired" }).Reason);
        Assert.Equal(ProofRejectionReason.KeyUnbound, Verifier.Verify(Input(proof) with { Session = null }).Reason);
    }

    [Fact]
    public void A_proof_presented_under_another_session_does_not_verify()
    {
        var input = Input(Proof(Fields())) with { Session = new SessionIdentity(Session.UserId, "session-b") };

        Assert.Equal(ProofRejectionReason.Invalid, Verifier.Verify(input).Reason);
    }

    [Theory]
    [InlineData("method")]
    [InlineData("path")]
    [InlineData("query")]
    [InlineData("body")]
    public void Tampering_with_any_request_field_is_detected(string field)
    {
        var input = Input(Proof(Fields()));
        var tampered = field switch
        {
            "method" => input with { Method = "PUT" },
            "path" => input with { Path = "/api/records/other" },
            "query" => input with { Query = "page=9" },
            _ => input with { BodyHash = SignatureCanonicalForm.HashBody("x"u8) },
        };

        Assert.Equal(ProofRejectionReason.Invalid, Verifier.Verify(tampered).Reason);
    }

    [Fact]
    public void A_method_outside_the_allowlist_is_invalid_not_an_exception()
    {
        Assert.Equal(ProofRejectionReason.Invalid, Verifier.Verify(Input(Proof(Fields())) with { Method = "TRACE" }).Reason);
    }

    [Fact]
    public void The_input_never_prints_the_proof()
    {
        var proof = Proof(Fields());

        Assert.DoesNotContain(proof, Input(proof).ToString(), StringComparison.Ordinal);
    }

    private SignedRequestFields Fields() => new(Audience, "POST", "/api/records", "page=1", SignatureCanonicalForm.HashBody([]), Unix(_clock.GetUtcNow()), RequestSigner.NewNonce());

    private string Proof(SignedRequestFields fields) => Signer.Proof(fields);

    private static ProofVerificationInput Input(string proof) =>
        new("POST", "/api/records", "page=1", SignatureCanonicalForm.HashBody([]), proof, Session.SessionKeyId, new SessionIdentity(Session.UserId, Session.SessionId));

    private static string Unix(DateTimeOffset time) => time.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
}

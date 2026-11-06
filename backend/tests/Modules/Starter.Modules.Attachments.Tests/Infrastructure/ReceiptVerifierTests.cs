using Codelab.Security.Uploads;
using Microsoft.Extensions.Options;
using Starter.Modules.Attachments.Infrastructure.Security;
using static Starter.Modules.Attachments.Tests.Infrastructure.InfrastructureFixtures;

namespace Starter.Modules.Attachments.Tests.Infrastructure;

public sealed class ReceiptVerifierTests
{
    [Fact]
    public void A_receipt_signed_under_the_ring_verifies_with_the_attested_metadata_and_the_allowlist_mime_type()
    {
        var ring = ReceiptRing();
        var token = new UploadReceiptSigner(ring, Protocol).Sign(Receipt(fileName: "Scan.PDF"));

        var verified = Verifier(ring).Verify(token);

        Assert.NotNull(verified);
        Assert.Equal(Scope, verified.Scope);
        Assert.Equal(FileId, verified.File.FileId);
        Assert.Equal("Scan.PDF", verified.File.FileName);
        Assert.Equal(2048, verified.File.SizeBytes);
        Assert.Equal(Sha256, verified.File.Sha256Hex);
        Assert.Equal(UploadContentTypes.Pdf, verified.File.ContentType);
    }

    [Fact]
    public void A_tampered_receipt_is_refused()
    {
        var ring = ReceiptRing();
        var token = new UploadReceiptSigner(ring, Protocol).Sign(Receipt());
        var parts = token.Split('.');
        var forged = new UploadReceiptSigner(ReceiptRing(), Protocol).Sign(Receipt() with { SizeBytes = 1 }).Split('.')[2];
        var tampered = string.Join('.', parts[0], parts[1], forged, parts[3]);

        Assert.Null(Verifier(ring).Verify(tampered));
    }

    [Fact]
    public void A_receipt_whose_mac_was_changed_is_refused()
    {
        var ring = ReceiptRing();
        var token = new UploadReceiptSigner(ring, Protocol).Sign(Receipt());
        var flipped = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        Assert.Null(Verifier(ring).Verify(flipped));
    }

    [Fact]
    public void A_receipt_signed_under_a_foreign_ring_with_the_same_key_id_is_refused()
    {
        var token = new UploadReceiptSigner(ReceiptRing(keyId: "k1"), Protocol).Sign(Receipt());

        Assert.Null(Verifier(ReceiptRing(keyId: "k1")).Verify(token));
    }

    [Fact]
    public void A_receipt_under_a_key_id_outside_the_ring_is_refused()
    {
        var token = new UploadReceiptSigner(ReceiptRing(keyId: "other"), Protocol).Sign(Receipt());

        Assert.Null(Verifier(ReceiptRing(keyId: "k1")).Verify(token));
    }

    [Fact]
    public void A_receipt_for_an_extension_without_a_mime_type_is_refused()
    {
        var ring = ReceiptRing();
        var token = new UploadReceiptSigner(ring, Protocol).Sign(Receipt(fileName: "notes.txt"));

        Assert.Null(Verifier(ring).Verify(token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("r1")]
    [InlineData("r1.k1.e30.AAAA")]
    [InlineData("not a receipt")]
    public void Malformed_receipts_are_refused(string receipt)
    {
        Assert.Null(Verifier(ReceiptRing()).Verify(receipt));
    }

    [Fact]
    public void With_receipts_disabled_nothing_verifies()
    {
        var ring = ReceiptRing();
        var token = new UploadReceiptSigner(ring, Protocol).Sign(Receipt());
        ring.ReceiptsEnabled = false;

        var verifier = Verifier(ring);

        Assert.False(verifier.IsEnabled);
        Assert.Null(verifier.Verify(token));
    }

    private static ReceiptVerifier Verifier(UploadSafetyOptions ring) => new(Options.Create(ring), Protocol);
}

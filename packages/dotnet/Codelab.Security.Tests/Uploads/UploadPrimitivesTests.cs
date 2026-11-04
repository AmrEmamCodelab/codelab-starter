using System.Security.Cryptography;
using System.Text;
using Codelab.Security.Keys;
using Codelab.Security.Tests.TestSupport;
using Codelab.Security.Uploads;

namespace Codelab.Security.Tests.Uploads;

public sealed class UploadPrimitivesTests
{
    private static readonly KeyRingOptions Ring = TestKeys.Ring("r1");

    [Theory]
    [InlineData("report.pdf", "report.pdf")]
    [InlineData("report.pdf. ", "report.pdf")]
    [InlineData("..\\..\\data-evil.pdf", "data-evil.pdf")]
    [InlineData("../../etc/evil.pdf", "evil.pdf")]
    [InlineData("C:\\temp\\x.png", "x.png")]
    [InlineData("dir/", null)]
    [InlineData("a.pdf:hidden.exe", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Only_the_normalised_leaf_of_a_client_name_is_kept(string? clientName, string? expected)
    {
        Assert.Equal(expected, FileNames.SafeLeafName(clientName));
    }

    [Theory]
    [InlineData("a.exe", true)]
    [InlineData("a.EXE.", true)]
    [InlineData("a.exe.pdf", false)]
    [InlineData("exe", false)]
    public void A_blocked_extension_is_judged_on_the_normalised_name(string fileName, bool blocked)
    {
        Assert.Equal(blocked, FileNames.IsBlockedExtension(fileName, new UploadSafetyOptions().BlockedExtensions));
    }

    [Fact]
    public void A_path_is_resolved_only_when_it_stays_under_the_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "codelab-containment-" + Guid.NewGuid().ToString("N"));

        Assert.True(FileNames.TryResolveWithin(root, Path.Combine("records", "a.pdf"), out var inside));
        Assert.StartsWith(Path.GetFullPath(root), inside, StringComparison.OrdinalIgnoreCase);
        Assert.False(FileNames.TryResolveWithin(root, Path.Combine("..", "escape.pdf"), out _));
        Assert.False(FileNames.TryResolveWithin(root, Path.GetFullPath(Path.Combine(root, "..", "abs.pdf")), out _));
        Assert.False(FileNames.TryResolveWithin(root, string.Empty, out _));
        Assert.False(FileNames.TryResolveWithin(root + "-sibling", Path.Combine("..", Path.GetFileName(root) + "x", "a.pdf"), out _));
    }

    [Fact]
    public void Caps_refuse_one_file_too_many_a_file_over_its_cap_and_a_total_over_its_cap()
    {
        var counter = new UploadCapCounter(new UploadSafetyOptions { MaxFileCount = 2, MaxFileBytes = 10, MaxTotalBytes = 15 });

        Assert.Equal(UploadCapVerdict.Accepted, counter.BeginFile());
        Assert.Equal(UploadCapVerdict.Accepted, counter.Record(10));
        Assert.Equal(UploadCapVerdict.FileTooLarge, counter.Record(1));
        Assert.Equal(UploadCapVerdict.Accepted, counter.BeginFile());
        Assert.Equal(UploadCapVerdict.TotalTooLarge, counter.Record(6));
        Assert.Equal(UploadCapVerdict.TooManyFiles, counter.BeginFile());
    }

    [Fact]
    public async Task The_upload_digest_is_lower_case_hex_sha256()
    {
        var content = Encoding.UTF8.GetBytes("content");

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), await UploadDigest.ComputeSha256HexAsync(new MemoryStream(content), CancellationToken.None));
    }

    [Fact]
    public void A_receipt_verifies_and_carries_exactly_what_was_signed()
    {
        var signer = new UploadReceiptSigner(Ring, TestProtocols.Test);
        var receipt = new UploadReceipt("records", "f-1", 1234, new string('a', 64), "report.pdf", 946684800);

        Assert.True(signer.TryVerify(signer.Sign(receipt), out var verified));
        Assert.Equal(receipt, verified);
    }

    [Fact]
    public void A_tampered_or_foreign_receipt_never_verifies()
    {
        var signer = new UploadReceiptSigner(Ring, TestProtocols.Test);
        var token = signer.Sign(new UploadReceipt("records", "f-1", 1234, new string('a', 64), "report.pdf", 946684800));
        var forgedPayload = Codelab.Security.Canonical.Base64UrlText.Encode(Encoding.UTF8.GetBytes("{\"Scope\":\"records\",\"FileId\":\"f-1\",\"SizeBytes\":1,\"Sha256Hex\":\"b\",\"FileName\":\"x\",\"IssuedAtUnix\":1}"));
        var parts = token.Split('.');

        Assert.False(signer.TryVerify(string.Join('.', parts[0], parts[1], forgedPayload, parts[3]), out _));
        Assert.False(new UploadReceiptSigner(TestKeys.Ring("r1"), TestProtocols.Test).TryVerify(token, out _));
        Assert.False(new UploadReceiptSigner(Ring, TestProtocols.Alternate).TryVerify(token, out _));
        Assert.False(signer.TryVerify("r1.r1.x", out _));
        Assert.False(signer.TryVerify(null, out _));
    }

    [Fact]
    public void A_receipt_signed_before_rotation_still_verifies_while_its_key_is_listed()
    {
        var old = new UploadReceiptSigner(Ring, TestProtocols.Test).Sign(new UploadReceipt("s", "f", 1, "h", "n", 1));
        var rotated = new KeyRingOptions { ActiveKeyId = "r2" };
        rotated.Keys.Add(Ring.Keys[0]);
        rotated.Keys.Add(new KeyEntryOptions { KeyId = "r2", MaterialBase64 = TestKeys.NewKey() });

        Assert.True(new UploadReceiptSigner(rotated, TestProtocols.Test).TryVerify(old, out _));
    }
}

using System.IO.Compression;
using Codelab.Security.Uploads;

namespace Codelab.Security.Tests.Uploads;

public sealed class FileSignatureSnifferTests
{
    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00];
    private static readonly byte[] ExecutableBytes = [0x4D, 0x5A, 0x90, 0x00];
    private readonly FileSignatureSniffer _sniffer = new(new UploadSafetyOptions().AllowedExtensions);

    [Theory]
    [InlineData("report.pdf")]
    [InlineData("scan.PNG")]
    [InlineData("photo.jpg")]
    [InlineData("photo.jpeg")]
    public void Exactly_the_allowed_extensions_are_accepted(string fileName)
    {
        Assert.True(_sniffer.IsAllowedExtension(fileName));
    }

    [Theory]
    [InlineData("tool.exe")]
    [InlineData("notes.txt")]
    [InlineData("archive.zip")]
    [InlineData("noextension")]
    [InlineData("report.pdf:evil.exe")]
    public void Every_other_extension_or_a_stream_suffix_is_refused(string fileName)
    {
        Assert.False(_sniffer.IsAllowedExtension(fileName));
    }

    [Theory]
    [InlineData("a.pdf", "pdf", true)]
    [InlineData("a.png", "png", true)]
    [InlineData("a.jpg", "jpeg", true)]
    [InlineData("a.png", "pdf", false)]
    [InlineData("a.pdf", "exe", false)]
    [InlineData("a.exe.pdf", "pdf", true)]
    [InlineData("a.pdf.exe", "pdf", false)]
    [InlineData("a.pdf. . ", "pdf", true)]
    public void The_content_must_match_the_last_extension_of_the_normalised_name(string fileName, string kind, bool expected)
    {
        Assert.Equal(expected, _sniffer.HasValidSignature(new MemoryStream(Bytes(kind)), fileName));
    }

    [Fact]
    public void A_file_shorter_than_its_signature_is_refused_and_exactly_the_signature_is_accepted()
    {
        Assert.False(_sniffer.HasValidSignature(new MemoryStream(PngBytes[..4]), "a.png"));
        Assert.True(_sniffer.HasValidSignature(new MemoryStream(PngBytes[..8]), "a.png"));
    }

    [Fact]
    public void A_seekable_stream_is_read_from_zero_and_its_position_restored()
    {
        using var stream = new MemoryStream(PdfBytes) { Position = 3 };

        Assert.True(_sniffer.HasValidSignature(stream, "a.pdf"));
        Assert.Equal(3, stream.Position);
    }

    [Fact]
    public void A_forward_only_stream_returning_one_byte_per_read_is_filled()
    {
        Assert.True(_sniffer.HasValidSignature(new TrickleStream(PngBytes), "a.png"));
    }

    [Fact]
    public void A_real_zip_and_an_empty_zip_are_containers_and_other_content_is_not()
    {
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("a.txt");
        }

        using var empty = new MemoryStream();
        new ZipArchive(empty, ZipArchiveMode.Create, leaveOpen: true).Dispose();

        Assert.True(FileSignatureSniffer.IsZipContainer(archive));
        Assert.True(FileSignatureSniffer.IsZipContainer(empty));
        Assert.False(FileSignatureSniffer.IsZipContainer(new MemoryStream(PdfBytes)));
    }

    [Fact]
    public void An_allowlist_entry_without_a_known_signature_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new FileSignatureSniffer([".pdf", ".docx"]));
    }

    private static byte[] Bytes(string kind) => kind switch
    {
        "pdf" => PdfBytes,
        "png" => PngBytes,
        "jpeg" => JpegBytes,
        _ => ExecutableBytes,
    };

    private sealed class TrickleStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;

        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 1));
    }
}

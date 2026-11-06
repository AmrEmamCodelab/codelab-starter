using Codelab.Security.Uploads;

namespace Codelab.Security.Tests.Uploads;

public sealed class UploadContentTypesTests
{
    [Fact]
    public void Every_known_signature_has_a_content_type()
    {
        var missing = FileSignatureSniffer.KnownSignatures.Keys.Where(extension => !UploadContentTypes.ByExtension.ContainsKey(extension)).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_content_type_belongs_to_a_known_signature()
    {
        var orphans = UploadContentTypes.ByExtension.Keys.Where(extension => !FileSignatureSniffer.KnownSignatures.ContainsKey(extension)).ToList();

        Assert.Empty(orphans);
    }

    [Theory]
    [InlineData("scan.pdf", UploadContentTypes.Pdf)]
    [InlineData("SCAN.PDF", UploadContentTypes.Pdf)]
    [InlineData(".pdf", UploadContentTypes.Pdf)]
    [InlineData("photo.png", UploadContentTypes.Png)]
    [InlineData("photo.jpg", UploadContentTypes.Jpeg)]
    [InlineData("photo.jpeg", UploadContentTypes.Jpeg)]
    [InlineData("bundle.zip", UploadContentTypes.Zip)]
    [InlineData("scan.pdf. ", UploadContentTypes.Pdf)]
    public void A_known_extension_maps_to_its_content_type(string fileName, string expected)
    {
        Assert.True(UploadContentTypes.TryGetContentType(fileName, out var contentType));
        Assert.Equal(expected, contentType);
    }

    [Theory]
    [InlineData("tool.exe")]
    [InlineData("page.html")]
    [InlineData("image.svg")]
    [InlineData("noextension")]
    [InlineData("a.pdf:hidden.exe")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_unknown_extension_has_no_content_type(string? fileName)
    {
        Assert.False(UploadContentTypes.TryGetContentType(fileName, out var contentType));
        Assert.Null(contentType);
    }
}

using Codelab.Attachments.Core.Storage;

namespace Codelab.Attachments.Core.Tests.Storage;

public sealed class AttachmentFileIdsTests
{
    [Fact]
    public void A_new_id_is_well_formed_url_safe_and_unique()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => AttachmentFileIds.New()).ToList();

        Assert.All(ids, id => Assert.True(AttachmentFileIds.IsWellFormed(id)));
        Assert.All(ids, id => Assert.Equal(22, id.Length));
        Assert.All(ids, id => Assert.Equal(id, Uri.EscapeDataString(id)));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAA..")]
    [InlineData(@"AAAAAAAAAAAAAAAAAAAA\x")]
    [InlineData("AAAAAAAAAAAAAAAAAAAA/x")]
    [InlineData("AAAAAAAAAAAAAAAAAAAA+x")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAA:x")]
    public void An_id_of_any_other_shape_is_malformed(string? fileId)
    {
        Assert.False(AttachmentFileIds.IsWellFormed(fileId));
    }
}

public sealed class AttachmentStorageOptionsValidatorTests
{
    private readonly AttachmentStorageOptionsValidator _validator = new();

    [Theory]
    [InlineData(@"D:\attachments")]
    [InlineData(@"\\files.example\attachments")]
    public void A_fully_qualified_folder_or_share_is_valid(string rootPath)
    {
        Assert.True(_validator.Validate(name: null, new AttachmentStorageOptions { RootPath = rootPath }).Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("attachments")]
    [InlineData(@"..\attachments")]
    [InlineData(@"D:attachments")]
    public void A_missing_or_relative_root_is_refused(string? rootPath)
    {
        var result = _validator.Validate(name: null, new AttachmentStorageOptions { RootPath = rootPath });

        Assert.True(result.Failed);
        Assert.Contains(AttachmentStorageOptions.SectionName, result.FailureMessage, StringComparison.Ordinal);
    }
}

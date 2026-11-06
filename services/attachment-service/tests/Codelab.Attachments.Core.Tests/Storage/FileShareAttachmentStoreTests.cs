using Codelab.Attachments.Core.Storage;
using Codelab.Attachments.Core.Tests.TestSupport;
using Codelab.Security.Uploads;
using Microsoft.Extensions.Time.Testing;

namespace Codelab.Attachments.Core.Tests.Storage;

public sealed class FileShareAttachmentStoreTests : IDisposable
{
    private readonly TempStorageRoot _root = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero));
    private readonly FileShareAttachmentStore _store;

    public FileShareAttachmentStoreTests() => _store = UploadFixture.NewStore(_root.Path, _clock);

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task A_committed_file_round_trips_with_its_metadata()
    {
        var content = TestFiles.Pdf();

        var metadata = await StoreAsync(content, "report.pdf");
        await using var stored = await _store.OpenReadAsync(metadata.FileId, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(metadata, stored.Metadata);
        Assert.Equal(new AttachmentFileMetadata(metadata.FileId, "report.pdf", content.Length, UploadContentTypes.Pdf, TestFiles.Sha256Hex(content), _clock.GetUtcNow()), metadata);
        using var copy = new MemoryStream();
        await stored.Content.CopyToAsync(copy);
        Assert.Equal(content, copy.ToArray());
    }

    [Fact]
    public async Task The_content_is_stored_under_the_year_and_month_as_the_bare_file_id()
    {
        var metadata = await StoreAsync(TestFiles.Pdf(), "report.pdf");

        var expected = Path.Combine(_root.Path, "2026", "03", metadata.FileId);
        Assert.Equal(
            [expected, expected + StorageLayout.MetadataSuffix],
            _root.AllFiles().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_file_stored_in_an_earlier_month_is_still_found()
    {
        var march = await StoreAsync(TestFiles.Pdf(), "march.pdf");
        _clock.Advance(TimeSpan.FromDays(60));
        var may = await StoreAsync(TestFiles.Pdf(), "may.pdf");

        await using var first = await _store.OpenReadAsync(march.FileId, CancellationToken.None);
        await using var second = await _store.OpenReadAsync(may.FileId, CancellationToken.None);

        Assert.Equal("march.pdf", first?.Metadata.FileName);
        Assert.Equal("may.pdf", second?.Metadata.FileName);
        Assert.True(Directory.Exists(Path.Combine(_root.Path, "2026", "05")));
    }

    [Theory]
    [InlineData(@"..\..\x.pdf", "x.pdf")]
    [InlineData(@"C:\Windows\x.pdf", "x.pdf")]
    [InlineData(@"\\files.example\share\x.pdf", "x.pdf")]
    [InlineData("a.pdf:evil", StorageLayout.FallbackFileName)]
    [InlineData("...", StorageLayout.FallbackFileName)]
    public async Task The_client_name_is_metadata_only_and_is_reduced_to_a_safe_leaf(string clientName, string expected)
    {
        var metadata = await StoreAsync(TestFiles.Pdf(), clientName);

        Assert.Equal(expected, metadata.FileName);
        Assert.All(_root.AllFiles(), path => Assert.StartsWith(_root.Path, path, StringComparison.OrdinalIgnoreCase));
        Assert.All(_root.AllFiles(), path => Assert.StartsWith(metadata.FileId, Path.GetFileName(path), StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_staged_file_is_invisible_until_committed_and_removed_when_disposed_uncommitted()
    {
        string fileId;
        await using (var staged = await _store.StageAsync(CancellationToken.None))
        {
            fileId = staged.FileId;
            await staged.Content.WriteAsync(TestFiles.Pdf());
            Assert.Null(await _store.OpenReadAsync(fileId, CancellationToken.None));
            Assert.Single(_root.AllFiles(), path => path.EndsWith(StorageLayout.StagedSuffix, StringComparison.Ordinal));
        }

        Assert.Empty(_root.AllFiles());
        Assert.Null(await _store.OpenReadAsync(fileId, CancellationToken.None));
    }

    [Fact]
    public async Task A_staged_file_commits_once()
    {
        await using var staged = await _store.StageAsync(CancellationToken.None);
        await staged.Content.WriteAsync(TestFiles.Pdf());
        var descriptor = new AttachmentFileDescriptor("a.pdf", UploadContentTypes.Pdf, "00");
        await staged.CommitAsync(descriptor, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => staged.CommitAsync(descriptor, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_removes_the_content_and_metadata_and_a_second_delete_finds_nothing()
    {
        var metadata = await StoreAsync(TestFiles.Pdf(), "report.pdf");

        Assert.True(await _store.DeleteAsync(metadata.FileId, CancellationToken.None));
        Assert.Empty(_root.AllFiles());
        Assert.False(await _store.DeleteAsync(metadata.FileId, CancellationToken.None));
        Assert.Null(await _store.OpenReadAsync(metadata.FileId, CancellationToken.None));
    }

    [Theory]
    [InlineData(@"..\..\Windows\win.ini")]
    [InlineData("../../etc/passwd")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData("2026/03/AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA.")]
    [InlineData("")]
    public async Task A_malformed_id_never_reaches_the_file_system(string fileId)
    {
        await StoreAsync(TestFiles.Pdf(), "report.pdf");

        Assert.Null(await _store.OpenReadAsync(fileId, CancellationToken.None));
        Assert.False(await _store.DeleteAsync(fileId, CancellationToken.None));
        Assert.Equal(2, _root.AllFiles().Count);
    }

    [Fact]
    public async Task An_unknown_id_and_a_missing_root_find_nothing()
    {
        var unknown = AttachmentFileIds.New();
        var missingRoot = UploadFixture.NewStore(Path.Combine(_root.Path, "not-created"), _clock);

        Assert.Null(await _store.OpenReadAsync(unknown, CancellationToken.None));
        Assert.Null(await missingRoot.OpenReadAsync(unknown, CancellationToken.None));
        Assert.False(await missingRoot.DeleteAsync(unknown, CancellationToken.None));
    }

    [Fact]
    public async Task Folders_that_are_not_year_and_month_numbers_are_ignored()
    {
        var metadata = await StoreAsync(TestFiles.Pdf(), "report.pdf");
        Directory.CreateDirectory(Path.Combine(_root.Path, "temp", "03"));
        Directory.CreateDirectory(Path.Combine(_root.Path, "2026", "3a"));

        await using var stored = await _store.OpenReadAsync(metadata.FileId, CancellationToken.None);

        Assert.NotNull(stored);
    }

    private async Task<AttachmentFileMetadata> StoreAsync(byte[] content, string fileName)
    {
        await using var staged = await _store.StageAsync(CancellationToken.None);
        await staged.Content.WriteAsync(content);
        return await staged.CommitAsync(new AttachmentFileDescriptor(fileName, UploadContentTypes.Pdf, TestFiles.Sha256Hex(content)), CancellationToken.None);
    }
}

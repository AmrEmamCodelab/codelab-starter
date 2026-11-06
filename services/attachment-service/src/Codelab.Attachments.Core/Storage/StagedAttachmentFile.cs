using Codelab.Security.Uploads;

namespace Codelab.Attachments.Core.Storage;

/// <summary>
/// A file staged as <c>&lt;fileId&gt;.partial</c> in its month folder (the same volume as its final place, so the
/// commit is an atomic rename). Commit moves the content, then writes the metadata; until the metadata exists the
/// file is invisible to readers. Dispose without commit deletes the staged content.
/// </summary>
internal sealed partial class StagedAttachmentFile(
    string fileId,
    DateTimeOffset storedAt,
    StoredFilePaths paths,
    FileStream content,
    ILogger logger) : IStagedAttachmentFile
{
    private bool _committed;

    public string FileId { get; } = fileId;

    public Stream Content => content;

    public async Task<AttachmentFileMetadata> CommitAsync(AttachmentFileDescriptor descriptor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (_committed)
        {
            throw new InvalidOperationException($"Staged file {FileId} was already committed.");
        }

        await content.FlushAsync(cancellationToken);
        var sizeBytes = content.Length;
        await content.DisposeAsync();
        var fileName = FileNames.SafeLeafName(descriptor.FileName) ?? StorageLayout.FallbackFileName;
        var metadata = new AttachmentFileMetadata(FileId, fileName, sizeBytes, descriptor.ContentType, descriptor.Sha256Hex, storedAt);

        File.Move(paths.StagedContent, paths.Content);
        try
        {
            await StorageLayout.WriteMetadataAsync(paths.Metadata, metadata, cancellationToken);
            _committed = true;
        }
        finally
        {
            if (!_committed)
            {
                File.Delete(paths.Content);
            }
        }

        LogCommitted(logger, FileId, sizeBytes);
        return metadata;
    }

    public async ValueTask DisposeAsync()
    {
        await content.DisposeAsync();
        if (_committed)
        {
            return;
        }

        try
        {
            File.Delete(paths.StagedContent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover .partial file is invisible to readers (it has no metadata) and never served.
            LogStagedNotDeleted(logger, ex, FileId);
        }
    }

    [LoggerMessage(EventId = 5302, Level = LogLevel.Information, Message = "Attachment file {FileId} stored ({SizeBytes} bytes)")]
    private static partial void LogCommitted(ILogger logger, string fileId, long sizeBytes);

    [LoggerMessage(EventId = 5303, Level = LogLevel.Warning, Message = "Staged attachment file {FileId} could not be deleted")]
    private static partial void LogStagedNotDeleted(ILogger logger, Exception exception, string fileId);
}

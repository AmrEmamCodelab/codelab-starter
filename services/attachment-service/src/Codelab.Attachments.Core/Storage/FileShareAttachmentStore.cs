using Microsoft.Extensions.Options;

namespace Codelab.Attachments.Core.Storage;

/// <summary>
/// <see cref="IAttachmentFileStore"/> on a local folder or a UNC share (<c>Attachments:Storage:RootPath</c>). Layout and
/// containment: <see cref="StorageLayout"/>. A lookup by id probes the month folders newest first, so its cost grows
/// with the number of months stored, not with the number of files.
/// </summary>
public sealed partial class FileShareAttachmentStore : IAttachmentFileStore
{
    /// <summary>The write buffer of staged content in bytes.</summary>
    internal const int ContentBufferBytes = 81920;

    private readonly string _root;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FileShareAttachmentStore> _logger;

    /// <summary>Creates the store over the validated storage options.</summary>
    public FileShareAttachmentStore(IOptions<AttachmentStorageOptions> options, TimeProvider timeProvider, ILogger<FileShareAttachmentStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.RootPath);
        _root = Path.GetFullPath(options.Value.RootPath);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<IStagedAttachmentFile> StageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fileId = AttachmentFileIds.New();
        var storedAt = _timeProvider.GetUtcNow();
        var paths = StorageLayout.PathsFor(_root, fileId, storedAt);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Content)!);
        var content = new FileStream(paths.StagedContent, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, ContentBufferBytes, FileOptions.Asynchronous);
        return Task.FromResult<IStagedAttachmentFile>(new StagedAttachmentFile(fileId, storedAt, paths, content, _logger));
    }

    /// <inheritdoc />
    public async Task<StoredAttachmentFile?> OpenReadAsync(string fileId, CancellationToken cancellationToken)
    {
        if (Locate(fileId) is not { } paths)
        {
            return null;
        }

        var metadata = await StorageLayout.ReadMetadataAsync(paths.Metadata, cancellationToken);
        if (metadata is null || !File.Exists(paths.Content))
        {
            return null;
        }

        var content = new FileStream(paths.Content, FileMode.Open, FileAccess.Read, FileShare.Read, ContentBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return new StoredAttachmentFile(metadata, content);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string fileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Locate(fileId) is not { } paths)
        {
            return Task.FromResult(false);
        }

        // Metadata first: once it is gone the file is invisible, so a failure between the two deletes leaves an
        // unreachable content file, never a listed file without content.
        File.Delete(paths.Metadata);
        File.Delete(paths.Content);
        LogDeleted(_logger, fileId);
        return Task.FromResult(true);
    }

    private StoredFilePaths? Locate(string fileId)
    {
        if (!AttachmentFileIds.IsWellFormed(fileId))
        {
            return null;
        }

        return StorageLayout.MonthFolders(_root)
            .Select(monthFolder => StorageLayout.PathsIn(_root, monthFolder, fileId))
            .FirstOrDefault(paths => File.Exists(paths.Metadata));
    }

    [LoggerMessage(EventId = 5301, Level = LogLevel.Information, Message = "Attachment file {FileId} deleted")]
    private static partial void LogDeleted(ILogger logger, string fileId);
}

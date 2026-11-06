namespace Codelab.Attachments.Core.Storage;

/// <summary>
/// The stored files. A write is staged first (a temp file next to its final place), then committed with an atomic
/// move, so a reader never sees a half-written file; a staged file that is not committed is deleted on dispose.
/// Every path is built from a service-issued id under the configured root; client names are metadata only.
/// </summary>
public interface IAttachmentFileStore
{
    /// <summary>Starts a new file under a fresh random id. The caller writes <see cref="IStagedAttachmentFile.Content"/>, then commits or disposes.</summary>
    Task<IStagedAttachmentFile> StageAsync(CancellationToken cancellationToken);

    /// <summary>The metadata and a read stream of the file; null when no committed file has this id (or the id is malformed).</summary>
    Task<StoredAttachmentFile?> OpenReadAsync(string fileId, CancellationToken cancellationToken);

    /// <summary>Deletes the file and its metadata; false when no committed file has this id (or the id is malformed).</summary>
    Task<bool> DeleteAsync(string fileId, CancellationToken cancellationToken);
}

/// <summary>A file being written. Not thread-safe; dispose it whether or not it was committed.</summary>
public interface IStagedAttachmentFile : IAsyncDisposable
{
    /// <summary>The id the file will have once committed.</summary>
    string FileId { get; }

    /// <summary>The staged content: writable, readable and seekable until <see cref="CommitAsync"/>.</summary>
    Stream Content { get; }

    /// <summary>Moves the content into place and writes its metadata; the size is the staged length.</summary>
    /// <exception cref="InvalidOperationException">The file was already committed.</exception>
    Task<AttachmentFileMetadata> CommitAsync(AttachmentFileDescriptor descriptor, CancellationToken cancellationToken);
}

/// <summary>What the uploader established about a staged file: its safe leaf name, its content type and its SHA-256.</summary>
public sealed record AttachmentFileDescriptor(string FileName, string ContentType, string Sha256Hex);

/// <summary>The metadata stored next to a file (the sidecar <c>&lt;fileId&gt;.meta.json</c>).</summary>
public sealed record AttachmentFileMetadata(string FileId, string FileName, long SizeBytes, string ContentType, string Sha256Hex, DateTimeOffset StoredAt);

/// <summary>A committed file opened for reading. The caller owns and disposes <see cref="Content"/>.</summary>
public sealed class StoredAttachmentFile(AttachmentFileMetadata metadata, Stream content) : IAsyncDisposable, IDisposable
{
    /// <summary>The stored metadata.</summary>
    public AttachmentFileMetadata Metadata { get; } = metadata;

    /// <summary>The file content, read-only.</summary>
    public Stream Content { get; } = content;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Content.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => Content.Dispose();
}

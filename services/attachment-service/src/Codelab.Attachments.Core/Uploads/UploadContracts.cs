namespace Codelab.Attachments.Core.Uploads;

/// <summary>Runs one multipart upload request through the safety checks into the store.</summary>
public interface IUploadPipeline
{
    /// <summary>
    /// Streams the file parts of <paramref name="request"/> one at a time into the store. All or nothing: on a refusal,
    /// a failure or cancellation, every file this request already stored is deleted again (files of other requests are
    /// never touched). The body is not read past the first refused part.
    /// </summary>
    Task<UploadOutcome> RunAsync(UploadRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// One upload request: the raw body, its <c>Content-Type</c> header, and the scope every receipt is bound to (the
/// opened upload ticket's subject; the API refuses a receipt whose scope is not the record it links to).
/// </summary>
public sealed record UploadRequest(Stream Body, string? ContentType, string ReceiptScope);

/// <summary>A stored file as the uploader sees it, with the signed receipt the API verifies at link time.</summary>
public sealed record UploadedFile(string FileId, string FileName, long SizeBytes, string ContentType, string Sha256, string Receipt);

/// <summary>The outcome of an upload: every stored file, or the one refusal that stopped the request.</summary>
public sealed record UploadOutcome
{
    private UploadOutcome(IReadOnlyList<UploadedFile> files, UploadRefusal? refusal)
    {
        Files = files;
        Refusal = refusal;
    }

    /// <summary>The stored files, in request order; empty when refused.</summary>
    public IReadOnlyList<UploadedFile> Files { get; }

    /// <summary>The refusal; null when every file was stored.</summary>
    public UploadRefusal? Refusal { get; }

    /// <summary>True when the request was refused and nothing was kept.</summary>
    public bool IsRefused => Refusal is not null;

    /// <summary>Every file stored.</summary>
    public static UploadOutcome Stored(IReadOnlyList<UploadedFile> files) => new(files ?? throw new ArgumentNullException(nameof(files)), refusal: null);

    /// <summary>Refused for <paramref name="reason"/>; nothing kept.</summary>
    public static UploadOutcome Refused(UploadRefusalReason reason) => new([], UploadRefusal.For(reason));
}

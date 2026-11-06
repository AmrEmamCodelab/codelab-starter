using System.Buffers;
using Codelab.Attachments.Core.Storage;
using Codelab.Security.Uploads;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Codelab.Attachments.Core.Uploads;

/// <summary>
/// The upload checks, in order, for each <c>file</c> part as it streams in: the file-count cap; the client name
/// reduced to a safe leaf, judged against the blocked list, the allowlist and the MIME table BEFORE a byte is written;
/// the per-file and total byte caps while copying (the crossing chunk is never written); empty content; the magic
/// bytes of the extension; then SHA-256, the atomic commit and a receipt signed for the request's scope. One request
/// is all or nothing (see <see cref="IUploadPipeline.RunAsync"/>).
/// </summary>
public sealed partial class UploadPipeline : IUploadPipeline
{
    /// <summary>The copy buffer in bytes; a cap is checked once per chunk read.</summary>
    internal const int CopyBufferBytes = 81920;

    private readonly IAttachmentFileStore _store;
    private readonly IUploadReceiptSigner _receiptSigner;
    private readonly UploadSafetyOptions _options;
    private readonly FileSignatureSniffer _sniffer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UploadPipeline> _logger;

    /// <summary>Creates the pipeline over the one upload allowlist (<c>Codelab:Security:Uploads</c>).</summary>
    public UploadPipeline(IAttachmentFileStore store, IUploadReceiptSigner receiptSigner, IOptions<UploadSafetyOptions> options, TimeProvider timeProvider, ILogger<UploadPipeline> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _receiptSigner = receiptSigner ?? throw new ArgumentNullException(nameof(receiptSigner));
        _options = options.Value;
        _sniffer = new FileSignatureSniffer(_options.AllowedExtensions);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<UploadOutcome> RunAsync(UploadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ReceiptScope);
        if (!MultipartParts.TryReadBoundary(request.ContentType, out var boundary))
        {
            return UploadOutcome.Refused(UploadRefusalReason.NotMultipart);
        }

        // The caps are enforced per chunk by UploadCapCounter, so the reader's own per-part limit is lifted.
        var reader = new MultipartReader(boundary, request.Body) { BodyLengthLimit = null };
        var progress = new UploadProgress(new UploadCapCounter(_options), request.ReceiptScope);
        UploadOutcome? outcome = null;
        try
        {
            outcome = await ReadAllAsync(reader, progress, cancellationToken);
            return outcome;
        }
        finally
        {
            if (outcome is not { IsRefused: false })
            {
                await RollbackAsync(progress.CommittedFileIds);
            }
        }
    }

    private async Task<UploadOutcome> ReadAllAsync(MultipartReader reader, UploadProgress progress, CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
            {
                if (await StoreSectionAsync(section, progress, cancellationToken) is { } reason)
                {
                    return UploadOutcome.Refused(reason);
                }
            }
        }
        catch (InvalidDataException)
        {
            // MultipartReader's verdict on a malformed body (bad framing, oversized part headers): a client error.
            return UploadOutcome.Refused(UploadRefusalReason.MalformedBody);
        }

        return progress.Files.Count == 0
            ? UploadOutcome.Refused(UploadRefusalReason.NoFile)
            : UploadOutcome.Stored([.. progress.Files]);
    }

    private async Task<UploadRefusalReason?> StoreSectionAsync(MultipartSection section, UploadProgress progress, CancellationToken cancellationToken)
    {
        if (!MultipartParts.TryReadFileName(section, out var clientFileName))
        {
            return UploadRefusalReason.UnexpectedPart;
        }

        if (progress.Counter.BeginFile() != UploadCapVerdict.Accepted)
        {
            return UploadRefusalReason.TooManyFiles;
        }

        if (!TryAcceptName(clientFileName, out var accepted))
        {
            return UploadRefusalReason.TypeNotAllowed;
        }

        await using var staged = await _store.StageAsync(cancellationToken);
        var refusal = await CopyWithinCapsAsync(section.Body, staged.Content, progress.Counter, cancellationToken)
            ?? CheckContent(staged.Content, accepted.FileName);
        if (refusal is null)
        {
            progress.Files.Add(await CommitAsync(staged, accepted, progress, cancellationToken));
        }

        return refusal;
    }

    private bool TryAcceptName(string clientFileName, out AttachmentFileDescriptor accepted)
    {
        var fileName = FileNames.SafeLeafName(clientFileName);
        string? contentType = null;
        var allowed = fileName is not null
            && !FileNames.IsBlockedExtension(fileName, _options.BlockedExtensions)
            && _sniffer.IsAllowedExtension(fileName)
            && UploadContentTypes.TryGetContentType(fileName, out contentType);
        accepted = new AttachmentFileDescriptor(fileName ?? string.Empty, contentType ?? string.Empty, Sha256Hex: string.Empty);
        return allowed;
    }

    private UploadRefusalReason? CheckContent(Stream content, string fileName)
    {
        if (content.Length == 0)
        {
            return UploadRefusalReason.EmptyFile;
        }

        return _sniffer.HasValidSignature(content, fileName) ? null : UploadRefusalReason.SignatureMismatch;
    }

    private async Task<UploadedFile> CommitAsync(IStagedAttachmentFile staged, AttachmentFileDescriptor accepted, UploadProgress progress, CancellationToken cancellationToken)
    {
        staged.Content.Position = 0;
        var sha256 = await UploadDigest.ComputeSha256HexAsync(staged.Content, cancellationToken);
        var metadata = await staged.CommitAsync(accepted with { Sha256Hex = sha256 }, cancellationToken);
        progress.CommittedFileIds.Add(metadata.FileId);
        var receipt = new UploadReceipt(
            Scope: progress.ReceiptScope,
            FileId: metadata.FileId,
            SizeBytes: metadata.SizeBytes,
            Sha256Hex: sha256,
            FileName: metadata.FileName,
            IssuedAtUnix: _timeProvider.GetUtcNow().ToUnixTimeSeconds());
        return new UploadedFile(metadata.FileId, metadata.FileName, metadata.SizeBytes, metadata.ContentType, sha256, _receiptSigner.Sign(receipt));
    }

    private static async Task<UploadRefusalReason?> CopyWithinCapsAsync(Stream source, Stream target, UploadCapCounter counter, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, CopyBufferBytes), cancellationToken)) > 0)
            {
                var verdict = counter.Record(read);
                if (verdict != UploadCapVerdict.Accepted)
                {
                    return verdict == UploadCapVerdict.TotalTooLarge ? UploadRefusalReason.TotalTooLarge : UploadRefusalReason.FileTooLarge;
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task RollbackAsync(IReadOnlyList<string> fileIds)
    {
        foreach (var fileId in fileIds)
        {
            try
            {
                // Not the request's token: a cancelled request must still remove what it stored.
                await _store.DeleteAsync(fileId, CancellationToken.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort per file: one stuck file must not keep the others of this request on the share.
                LogRollbackFailed(_logger, ex, fileId);
            }
        }
    }

    [LoggerMessage(EventId = 5311, Level = LogLevel.Warning, Message = "Attachment file {FileId} of a refused upload could not be removed")]
    private static partial void LogRollbackFailed(ILogger logger, Exception exception, string fileId);

    /// <summary>The per-request state: the caps, the receipt scope, the files stored so far and the ids to roll back.</summary>
    private sealed record UploadProgress(UploadCapCounter Counter, string ReceiptScope)
    {
        public List<UploadedFile> Files { get; } = [];

        public List<string> CommittedFileIds { get; } = [];
    }
}
